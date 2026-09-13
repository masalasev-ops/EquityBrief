using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EquityBrief.Core.Providers;

// The filings archive, read.
//
// Written against nine captured responses from two filers and four more from two
// others, and not the other way round, which is the rule 1.6 and 1.7 set and 6.1
// followed. Ten things here would have been written wrong from the endpoint names
// and the corpus's own description of what the archive holds.
//
// The company's filing index is not a list of filings. `filings.recent` is a set
// of parallel arrays, one per field, so every array holds the same positions and a
// reader taking it for a list of objects finds none.
//
// A filing's directory listing carries a `type` field and it is a display icon,
// `text.gif`. The kind of a document is stated on the filing's own index page and
// nowhere else, which is why the route fetches a page a reader would not think to
// ask for.
//
// The earnings release is typed `EX-99.1` by two of the four captured filers and
// `EX-99` by the other two, so a reader keyed on the longer spelling finds no
// release for a sixth of the index and reports that none was filed. And a filing
// may index two of them, so the release is the lower sequence and never the only
// match.
//
// A filing's documents are served inside the archive's own SGML envelope, which
// the request for a bare file name does not remove. A reader taking the response
// for HTML parses a `<TYPE>` line and a `<FILENAME>` line before the page, and the
// type line's text reads as body text.
//
// The filing lists 47 and 89 rendered reports and four and six of them mention
// segments. Taking the first whose name matches takes a narrative note. Which one
// holds figures is a property of the table rather than of its name: the first
// candidate in the Details category is the right one for one captured filer and
// carries a single boolean for the other.
//
// The segment table's scale is stated once, in its own title cell, and its figures
// are in millions. Negatives are parenthesised rather than signed and empty cells
// are non-breaking spaces.
//
// One row of a money table is not money: a count of reportable segments, marked by
// a unit after its label. A reader applying the table's scale records two million
// segments.
//
// Neither the row label nor the concept identifies a group. Four groups of one
// captured table share one axis and member in the markup while their labels
// differ, and two labels appear twice.
//
// Management's guidance is prose under a heading in an exhibit, in no structured
// field anywhere in the filing, and a heading locates it for five of twelve filers
// measured. So it is stored as the passage and never parsed into a figure.
// see: Guidance is stored as management's own prose and never parsed into a figure
//
// And the archive files revenue under several concepts at once without keeping
// them current: over both captured filers whole, one carries `Revenues` to 2018
// and its contract revenue to 2026 while the other is the reverse, and both
// retired `SalesRevenueNet` in 2018. So the facts are kept under the concept each
// was filed against and nothing here maps them to a revenue.
public static class SecEdgarArchive
{
    // The structured endpoints answer on one host and the filing documents on the
    // other. Both are the archive.
    public const string DataHost = "data.sec.gov";

    public const string DocumentHost = "www.sec.gov";

    // The 8-K item that makes a filing a results announcement, which is the only
    // thing in the index that says the earnings release is attached to it.
    public const string ResultsItem = "2.02";

    // The forms that carry a segment table, in the order a reader prefers them: a
    // quarter is more recent than a year and both carry one.
    public static readonly string[] PeriodicForms = ["10-Q", "10-K"];

    // The exhibit type the release and the transcript are both filed under. A
    // prefix, because two of four captured filers write it with no point suffix,
    // and the rule that picks between two matches is the sequence rather than the
    // spelling.
    public const string ExhibitType = "EX-99";

    // How many report pages one read will fetch looking for figures by segment.
    //
    // Four, because the two captured filings need one and two and the wider of the
    // two carries four candidates in the Details category. A cap rather than the
    // whole list, so a filing with thirty segment-named reports cannot turn one
    // open into thirty requests, and the count actually read is carried on the
    // result rather than assumed.
    public const int SegmentReportsAtMost = 4;

    // What the numbers section asks the archive for, named so a part not served is
    // a value rather than a missing key. `FundamentalsFetcher.Parts` holds the same
    // two spellings, which is what lets one source column answer about both feeds.
    public const string Segments = "segments";

    public const string Guidance = "guidance";

    public const string Facts = "facts";

    // One read of one name's archive, in the order the route derives.
    //
    // Six requests where every part is served: the company's filing index, the
    // results announcement's index page, the release exhibit, the periodic
    // filing's report list, one report page, and the company's facts. A filing
    // whose first segment candidate carries no figures costs one more.
    public static async Task<ArchiveFilings> ReadAsync(
        ArchiveFetch fetch,
        string ticker,
        string cik,
        CancellationToken cancellation = default)
    {
        var padded = Padded(cik);

        var submissions = await fetch(Request(ArchiveDocument.Submissions, padded), cancellation).ConfigureAwait(false)
            ?? throw new ProviderRefusal(
                $"The archive serves no filing index for CIK {padded}. Every route into the archive "
                + "starts there, so a name whose index is absent is a name with no filings rather than "
                + "one whose filings could not be read.",
                transient: false);

        var filings = Filings(submissions);
        var periodic = filings.Where(filing => PeriodicForms.Contains(filing.Form, StringComparer.Ordinal)).ToArray();
        var results = filings.Where(Announces).ToArray();

        var notCarried = new List<string>();

        var (guidance, transcript) = await GuidanceAsync(fetch, padded, results, notCarried, cancellation).ConfigureAwait(false);
        var (segments, read) = await SegmentsAsync(fetch, padded, periodic, notCarried, cancellation).ConfigureAwait(false);
        var facts = await FactsAsync(fetch, padded, notCarried, cancellation).ConfigureAwait(false);

        return new ArchiveFilings(
            ticker, padded, periodic, results, segments, guidance, facts, transcript, notCarried, read);
    }

    // Whether a filing is a results announcement, which is an 8-K carrying item
    // 2.02. Read off the item list as a whole item rather than as a substring, so
    // item 2.021 would not answer for it and neither would a file number that
    // happens to contain the digits.
    public static bool Announces(IndexedFiling filing) =>
        filing.Items.Contains(ResultsItem, StringComparer.Ordinal);

    // The CIK as the archive addresses it, which is ten digits with leading zeros.
    //
    // One captured payload sends it padded and another sends the same value as an
    // unpadded number, so the two would address two different companies if either
    // were used as it arrived.
    public static string Padded(string cik)
    {
        var digits = new string([.. (cik ?? string.Empty).Where(char.IsAsciiDigit)]);

        return digits.Length == 0
            ? throw new ProviderRefusal(
                "The archive is addressed by CIK and nothing else, and none was given. A read with no "
                + "identifier would be a read of whatever the archive answered, so it is refused here "
                + "rather than sent.",
                transient: false)
            : digits.TrimStart('0').PadLeft(10, '0');
    }

    // The CIK as a filing's own path spells it, which is the same digits with the
    // leading zeros taken off.
    public static string Bare(string padded) =>
        padded.TrimStart('0') is { Length: > 0 } digits ? digits : "0";

    public static ArchiveRequest Request(ArchiveDocument document, string padded, string? within = null, string? file = null) =>
        document switch
        {
            ArchiveDocument.Submissions => new(document, DataHost, "submissions/CIK" + padded + ".json"),
            ArchiveDocument.CompanyFacts => new(document, DataHost, "api/xbrl/companyfacts/CIK" + padded + ".json"),
            _ => new(document, DocumentHost, Folder(padded, within) + "/" + (file ?? string.Empty)),
        };

    // Where one filing's documents live, which is the bare CIK and the accession
    // number with its dashes taken out.
    public static string Folder(string padded, string? accession) =>
        "Archives/edgar/data/" + Bare(padded) + "/" + (accession ?? string.Empty).Replace("-", string.Empty, StringComparison.Ordinal);

    public const string ReportListFile = "FilingSummary.xml";

    // ---- the company's filing index -------------------------------------------

    // Every filing the index's recent window holds.
    //
    // The arrays are parallel, one per field, and the position is the filing. A
    // field the payload does not carry leaves every filing without it rather than
    // shortening the list, so the count comes from the form array and each field
    // is read at that position or treated as absent.
    public static IReadOnlyList<IndexedFiling> Filings(string submissions)
    {
        using var document = JsonDocument.Parse(submissions);

        if (!document.RootElement.TryGetProperty("filings", out var all)
            || !all.TryGetProperty("recent", out var recent)
            || !recent.TryGetProperty("form", out var forms)
            || forms.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var count = forms.GetArrayLength();
        var filings = new List<IndexedFiling>(count);

        for (var at = 0; at < count; at++)
        {
            var filed = Day(Text(recent, "filingDate", at));

            if (filed is not { } filingDate)
            {
                continue;
            }

            filings.Add(new IndexedFiling(
                Text(recent, "form", at) ?? string.Empty,
                Text(recent, "accessionNumber", at) ?? string.Empty,
                filingDate,
                Day(Text(recent, "reportDate", at)),
                Items(Text(recent, "items", at)),
                Text(recent, "primaryDocument", at) ?? string.Empty));
        }

        return filings;
    }

    // The company's own identifier, out of its index. Padded here because the two
    // structured endpoints disagree about how to send it.
    public static string CikOf(string payload)
    {
        using var document = JsonDocument.Parse(payload);

        return document.RootElement.TryGetProperty("cik", out var cik)
            ? Padded(cik.ValueKind == JsonValueKind.Number
                ? cik.GetInt64().ToString(CultureInfo.InvariantCulture)
                : cik.GetString() ?? string.Empty)
            : throw new ProviderRefusal(
                "The payload carries no CIK, so nothing in it can be addressed.", transient: false);
    }

    static IReadOnlyList<string> Items(string? items) =>
        string.IsNullOrWhiteSpace(items)
            ? []
            : [.. items.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];

    static string? Text(JsonElement recent, string field, int at) =>
        recent.TryGetProperty(field, out var array)
            && array.ValueKind == JsonValueKind.Array
            && at < array.GetArrayLength()
                ? array[at].ValueKind == JsonValueKind.String ? array[at].GetString() : null
                : null;

    // ---- a filing's own index page --------------------------------------------

    // Every document a filing indexes, with the kind stated.
    //
    // Read off the document-format table alone. The page carries a second table of
    // data files, and the taxonomy documents in it are typed `EX-101.SCH` and the
    // like, so a reader taking both tables finds exhibits that are not exhibits.
    public static IReadOnlyList<FiledDocument> Documents(string indexPage)
    {
        var table = Regex.Match(
            Unwrapped(indexPage),
            "<table[^>]*summary=\"Document Format Files\".*?</table>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        if (!table.Success)
        {
            return [];
        }

        var documents = new List<FiledDocument>();

        foreach (var row in Rows(table.Value))
        {
            var cells = Regex.Matches(row, "<td[^>]*>(.*?)</td>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (cells.Count < 4 || !int.TryParse(Plain(cells[0].Groups[1].Value), CultureInfo.InvariantCulture, out var sequence))
            {
                // The last row is the whole submission as one file and its
                // sequence cell is a non-breaking space, so a reader that took
                // every row would offer the complete filing as a document.
                continue;
            }

            documents.Add(new FiledDocument(
                sequence,
                Plain(cells[3].Groups[1].Value),
                Plain(cells[1].Groups[1].Value),
                Linked(cells[2].Groups[1].Value)));
        }

        return documents;
    }

    // The earnings release among a filing's documents: the exhibit type, and the
    // lowest sequence where a filing carries more than one.
    public static FiledDocument? Release(IReadOnlyList<FiledDocument> documents) =>
        documents
            .Where(Exhibit)
            .Where(document => !NamesATranscript(document))
            .OrderBy(document => document.Sequence)
            .FirstOrDefault();

    // The call transcript, where a company files one.
    //
    // Located rather than parsed, because none of the twelve filers measured at
    // 6.2 filed one. Nothing depends on it and its absence is the ordinary case.
    // see: Transcripts are opportunistic, never a dependency
    public static FiledDocument? Transcript(IReadOnlyList<FiledDocument> documents) =>
        documents.Where(Exhibit).Where(NamesATranscript).OrderBy(document => document.Sequence).FirstOrDefault();

    static bool Exhibit(FiledDocument document) =>
        document.Type.StartsWith(ExhibitType, StringComparison.OrdinalIgnoreCase);

    static bool NamesATranscript(FiledDocument document) =>
        document.Description.Contains("transcript", StringComparison.OrdinalIgnoreCase)
            || document.FileName.Contains("transcript", StringComparison.OrdinalIgnoreCase);

    // The file name a cell links to, which is the last segment of its own link.
    //
    // Taken from the link rather than from the cell's text for two reasons the
    // captures show: the primary document's link goes through the inline viewer as
    // a query string, and the cell's text carries a rendering note after the name.
    static string Linked(string cell)
    {
        var href = Regex.Match(cell, "href=\"([^\"]+)\"", RegexOptions.IgnoreCase);
        var path = href.Success ? href.Groups[1].Value : Plain(cell);
        var at = path.LastIndexOf('/');

        return at >= 0 ? path[(at + 1)..] : path;
    }

    // ---- the filing's report list ---------------------------------------------

    // Which rendered reports might hold figures by segment, in the order the
    // filing states them.
    //
    // Named and categorised, which narrows 47 reports to two and 89 to four, and
    // no further: which of those holds figures is decided by reading the table.
    public static IReadOnlyList<string> SegmentCandidates(string reportList)
    {
        var candidates = new List<string>();

        foreach (Match report in Regex.Matches(reportList, "<Report\\b.*?</Report>", RegexOptions.Singleline))
        {
            var shortName = Element(report.Value, "ShortName");
            var category = Element(report.Value, "MenuCategory");
            var file = Element(report.Value, "HtmlFileName");

            if (file is { Length: > 0 }
                && shortName is not null
                && shortName.Contains("segment", StringComparison.OrdinalIgnoreCase)
                && string.Equals(category, "Details", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(file);
            }
        }

        return candidates;
    }

    static string? Element(string block, string name)
    {
        var found = Regex.Match(block, "<" + name + ">(.*?)</" + name + ">", RegexOptions.Singleline);

        return found.Success ? Plain(found.Groups[1].Value) : null;
    }

    // ---- one rendered report page ---------------------------------------------

    // A segment table, or nothing where the page carries none.
    //
    // What makes a page a segment table is that it groups rows under a dimension
    // and states a figure under at least one of them. The first candidate of one
    // captured filing passes both tests and the first of the other carries one
    // period, an abstract row and a single boolean, which is why the choice is
    // made here and not from the report's name.
    public static SegmentBreakdown? Breakdown(string reportPage, string report)
    {
        var body = Unwrapped(reportPage);
        var rows = Rows(body);

        if (rows.Count == 0)
        {
            return null;
        }

        var title = Plain(Regex.Match(rows[0], "<th[^>]*class=\"tl\"[^>]*>(.*?)</th>", RegexOptions.IgnoreCase | RegexOptions.Singleline).Groups[1].Value);
        var scale = Scale(title);
        var periods = Periods(rows);

        if (periods.Count == 0)
        {
            return null;
        }

        var consolidated = new List<SegmentFigure>();
        var groups = new List<SegmentGroup>();
        var current = (Label: string.Empty, Dimension: (string?)null, Figures: new List<SegmentFigure>());
        var open = false;

        foreach (var row in rows)
        {
            var kind = RowClass(row);

            if (kind == GroupRow)
            {
                if (open)
                {
                    groups.Add(new SegmentGroup(current.Label, current.Dimension, current.Figures));
                }

                current = (Label(row), Dimension(row), []);
                open = true;

                continue;
            }

            if (!ValueRows.Contains(kind, StringComparer.Ordinal))
            {
                continue;
            }

            var figures = Figures(row, periods, scale);

            (open ? current.Figures : consolidated).AddRange(figures);
        }

        if (open)
        {
            groups.Add(new SegmentGroup(current.Label, current.Dimension, current.Figures));
        }

        var carries = groups.Any(group => group.Figures.Any(figure => figure.Value is not null));

        return carries
            ? new SegmentBreakdown(report, title, scale, periods, consolidated, groups)
            : null;
    }

    // What the table's own title says its figures are in, as a multiplier.
    //
    // Stated once for the whole table and applied to every money row in it. A
    // table stating no scale states its figures as filed, which is the case for
    // the candidate that carries no money at all.
    public static int Scale(string title) =>
        title.Contains("in Billions", StringComparison.OrdinalIgnoreCase) ? 1_000_000_000
            : title.Contains("in Millions", StringComparison.OrdinalIgnoreCase) ? 1_000_000
            : title.Contains("in Thousands", StringComparison.OrdinalIgnoreCase) ? 1_000
            : 1;

    const string GroupRow = "rh";

    static readonly string[] ValueRows = ["re", "ro", "reu", "rou"];

    static string RowClass(string row)
    {
        var found = Regex.Match(row, "^<tr[^>]*class=\"([^\"]*)\"", RegexOptions.IgnoreCase);

        return found.Success ? found.Groups[1].Value.Trim() : string.Empty;
    }

    // The period columns, read off the header's own column spans.
    //
    // Two header rows: the first names each period and says how many columns it
    // covers, the second holds the dates. So the date at a position belongs to the
    // period whose span reaches it, which is how one end date under two spans
    // stops being one column.
    static IReadOnlyList<ReportPeriod> Periods(IReadOnlyList<string> rows)
    {
        var spans = new List<(int Months, int Columns)>();

        foreach (Match cell in Regex.Matches(rows[0], "<th([^>]*)>(.*?)</th>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var attributes = cell.Groups[1].Value;

            if (attributes.Contains("class=\"tl\"", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var months = Regex.Match(Plain(cell.Groups[2].Value), "([0-9]+) Months? Ended", RegexOptions.IgnoreCase);
            var span = Regex.Match(attributes, "colspan=\"([0-9]+)\"", RegexOptions.IgnoreCase);

            spans.Add((
                months.Success ? int.Parse(months.Groups[1].Value, CultureInfo.InvariantCulture) : 0,
                span.Success ? int.Parse(span.Groups[1].Value, CultureInfo.InvariantCulture) : 1));
        }

        var dates = rows.Count > 1
            ? Regex.Matches(rows[1], "<th[^>]*>(.*?)</th>", RegexOptions.IgnoreCase | RegexOptions.Singleline)
                .Select(cell => Rendered(cell.Groups[1].Value))
                .ToArray()
            : [];

        var periods = new List<ReportPeriod>();
        var at = 0;

        foreach (var (months, columns) in spans)
        {
            for (var column = 0; column < columns; column++, at++)
            {
                if (at < dates.Length && dates[at] is { } ended)
                {
                    periods.Add(new ReportPeriod(months, ended));
                }
            }
        }

        return periods;
    }

    static string Label(string row) =>
        Plain(Regex.Match(row, "<td[^>]*class=\"pl[^\"]*\"[^>]*>(.*?)</td>", RegexOptions.IgnoreCase | RegexOptions.Singleline).Groups[1].Value);

    // The axis and member the markup carries for a row, which several groups of
    // one table share. Kept because it is the only machine-readable thing about a
    // grouping, and never used to tell two groups apart.
    static string? Dimension(string row)
    {
        var found = Regex.Match(row, "defref_([^']+)", RegexOptions.IgnoreCase);

        return found.Success && found.Groups[1].Value.Contains('=', StringComparison.Ordinal)
            ? found.Groups[1].Value
            : null;
    }

    // One row's figures, one per period column.
    //
    // A row whose label ends in a unit after a vertical bar is not in the table's
    // currency, and the scale is not applied to it. A row whose label is bracketed
    // and whose cells are all empty is the renderer's own structural row and
    // carries nothing.
    static IReadOnlyList<SegmentFigure> Figures(string row, IReadOnlyList<ReportPeriod> periods, int scale)
    {
        var label = Label(row);

        if (label.Length == 0)
        {
            return [];
        }

        var unit = Unit(ref label);
        var concept = Concept(row);

        // The scale is a statement about the money in the table, so a row stating
        // a unit of its own is left exactly as filed.
        var applies = unit is null ? scale : 1;
        var cells = Regex
            .Matches(row, "<td[^>]*class=\"(num|nump|text)\"[^>]*>(.*?)</td>", RegexOptions.IgnoreCase | RegexOptions.Singleline)
            .Select(cell => Plain(cell.Groups[2].Value))
            .ToArray();

        if (cells.Length == 0
            || ((label.StartsWith('[') || label.EndsWith(']')) && cells.All(cell => cell.Length == 0)))
        {
            return [];
        }

        var figures = new List<SegmentFigure>();

        for (var at = 0; at < cells.Length && at < periods.Count; at++)
        {
            figures.Add(new SegmentFigure(concept, label, unit, periods[at], Figure(cells[at], applies)));
        }

        return figures;
    }

    // The unit a row states for itself, taken off the end of its label.
    //
    // The renderer puts it after a vertical bar, and a group header uses the same
    // separator between two members, which is why this is only ever asked of a
    // value row.
    static string? Unit(ref string label)
    {
        var at = label.LastIndexOf('|');

        if (at < 0)
        {
            return null;
        }

        var unit = label[(at + 1)..].Trim();

        label = label[..at].Trim();

        return unit.Length > 0 ? unit : null;
    }

    static string Concept(string row)
    {
        var found = Regex.Match(row, "defref_([A-Za-z0-9_\\-]+)", RegexOptions.IgnoreCase);

        return found.Success ? found.Groups[1].Value : string.Empty;
    }

    // ---- the company's facts --------------------------------------------------

    // Every figure the archive holds, under the concept it was filed against.
    //
    // Not mapped to a revenue or an earnings figure, because the archive files
    // revenue under several concepts at once and does not keep them all current.
    public static IReadOnlyList<ArchiveFact> FactsOf(string companyFacts)
    {
        using var document = JsonDocument.Parse(companyFacts);

        if (!document.RootElement.TryGetProperty("facts", out var taxonomies))
        {
            return [];
        }

        var facts = new List<ArchiveFact>();

        foreach (var taxonomy in taxonomies.EnumerateObject())
        {
            foreach (var concept in taxonomy.Value.EnumerateObject())
            {
                if (!concept.Value.TryGetProperty("units", out var units))
                {
                    continue;
                }

                foreach (var unit in units.EnumerateObject())
                {
                    foreach (var fact in unit.Value.EnumerateArray())
                    {
                        // The start is optional and the end is not. An instant
                        // fact carries no start at all, which every balance-sheet
                        // figure is, so requiring both drops that whole class and
                        // leaves a reading that looks complete.
                        var start = Day(Stated(fact, "start"));
                        var end = Day(Stated(fact, "end"));
                        var filed = Day(Stated(fact, "filed"));

                        if (end is not { } to || filed is not { } on
                            || !fact.TryGetProperty("val", out var value)
                            || value.ValueKind != JsonValueKind.Number)
                        {
                            continue;
                        }

                        facts.Add(new ArchiveFact(
                            concept.Name, unit.Name, start, to, on,
                            Stated(fact, "frame"), Stated(fact, "accn") ?? string.Empty,
                            value.GetDecimal()));
                    }
                }
            }
        }

        return facts;
    }

    // The facts the archive marks as a quarter of their own, with a period restated
    // by a later filing taken at the later filing.
    //
    // Three readings the payload makes necessary. A fiscal period label sits on a
    // three-month figure and on the nine months that contain it, so the label cannot
    // say which; the frame the archive assigns names a calendar quarter and only ever
    // sits on the shorter one. One period appears twice under two accession numbers
    // where a filing restated it, so a reader that does not take the later filing
    // counts one quarter twice. And the frame comes in two forms, a span and an
    // instant, the second written with an I after the quarter: every balance-sheet
    // figure is an instant, so a reader that matched the span form alone would keep
    // revenue and earnings and drop every asset and liability line without saying so.
    public static IReadOnlyList<ArchiveFact> Quarterly(IReadOnlyList<ArchiveFact> facts) =>
    [
        .. facts
            .Where(fact => Kept.Contains(fact.Concept, StringComparer.Ordinal))
            .Where(fact => fact.Frame is { } frame && QuarterFrame.IsMatch(frame))
            .GroupBy(fact => (fact.Concept, fact.Unit, fact.Start, fact.End))
            .Select(period => period.OrderByDescending(fact => fact.Filed).First())
            .GroupBy(fact => (fact.Concept, fact.Unit))
            .SelectMany(concept => concept.OrderByDescending(fact => fact.End).Take(QuartersKept))
            .OrderBy(fact => fact.Concept, StringComparer.Ordinal)
            .ThenBy(fact => fact.End),
    ];

    // Which concepts are kept, named rather than taken whole.
    //
    // The archive holds 503 and 649 concepts for the two captured filers, and
    // keeping every quarterly fact of every one would put megabytes of a payload on
    // one store row for figures nothing reads. So the set is what the numbers
    // section's own figures correspond to, which is what makes these facts worth
    // keeping at all: the archive's filed figure for a period sits beside the
    // vendor's for the same period, from the primary source, with the filing that
    // stated it (see: Code owns every number).
    //
    // Revenue is three concepts rather than one and that is the measured reason the
    // set is a set. Over both captured payloads whole, one filer carries `Revenues`
    // to 2018 and its contract revenue to 2026 while the other is the reverse, and
    // both retired `SalesRevenueNet` in 2018. Picking one spelling reads an
    // eight-year-old revenue for one filer, and mapping the three into a revenue is
    // a choice this checkpoint does not make: each is kept under the concept it was
    // filed against.
    //
    // A concept added here needs a capture that carries it, because a member nothing
    // exercises is a member that can be misspelled without anything failing. The
    // committed captures were trimmed to exactly this set for that reason.
    public static readonly string[] Kept =
    [
        // Revenue, in the three spellings the archive uses for it.
        "Revenues",
        "RevenueFromContractWithCustomerExcludingAssessedTax",
        "SalesRevenueNet",
        // Earnings.
        "NetIncomeLoss",
        // The balance sheet's own total, which is an instant and not a span.
        "Assets",
        // The share count the market value rests on, and the float, both filed
        // under the entity taxonomy rather than the accounting one.
        "EntityCommonStockSharesOutstanding",
        "EntityPublicFloat",
    ];

    // How many quarters of each concept are kept.
    //
    // The same window the filings are stored over, so a reading across twelve
    // quarters has both providers' figures for the same span rather than one
    // provider's twelve against the other's whatever-the-payload-held. The ruling
    // that sets the number is the store's and this is the archive agreeing with it,
    // which a test asserts rather than a comment claiming.
    // see: Twelve filings are stored and five are shown
    public const int QuartersKept = 12;

    static readonly Regex QuarterFrame = new("^CY[0-9]{4}Q[1-4]I?$", RegexOptions.Compiled);

    // Whether a fact is as of an instant rather than over a span, which the archive
    // says twice: by sending no start date, and by the I on the frame. Both are
    // asserted against each other, so a payload that stopped agreeing with itself
    // fails rather than being resolved one way.
    public static bool IsAnInstant(ArchiveFact fact) => fact.Start is null;

    static string? Stated(JsonElement fact, string field) =>
        fact.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    // ---- the release exhibit --------------------------------------------------

    // Management's own forecast, located where a heading locates it.
    //
    // A heading is an emphasised block whose whole text ends in the word outlook or
    // guidance. Over twelve filers, five carry one and at least two of the other
    // seven state guidance inside an ordinary paragraph or a quotation, so a
    // passage nobody located is never reported as a company that gave none.
    //
    // Keyed on a block rather than on the words, because the words are everywhere:
    // one captured exhibit uses outlook four times, in a subtitle, in a quotation,
    // in the heading and in a sentence about a webcast, and the word guidance
    // appears in it once, inside the forward-looking-statements boilerplate. A
    // reader keyed on either word extracts the legal disclaimer.
    public static FiledGuidance Locate(string exhibit, string document, DateOnly filedOn)
    {
        var blocks = Blocks(Unwrapped(exhibit));
        var at = blocks.FindIndex(block => block.Emphasised && IsHeading(block.Text));

        if (at < 0)
        {
            return new FiledGuidance(document, filedOn, null, null);
        }

        var passage = new List<string>();

        foreach (var block in blocks.Skip(at + 1))
        {
            if (block.Emphasised && block.Text.Split(' ').Length <= HeadingWords)
            {
                break;
            }

            passage.Add(block.Text);
        }

        return new FiledGuidance(
            document, filedOn, blocks[at].Text,
            passage.Count > 0 ? string.Join("\n", passage) : null);
    }

    // At most eight words, which is what separates a heading from the subtitle of
    // one captured exhibit: that one ends in the word improved and runs to eleven
    // words, so both halves of the test are doing work.
    public const int HeadingWords = 8;

    static bool IsHeading(string text) =>
        text.Split(' ') is { Length: > 0 and <= HeadingWords } words
            && (words[^1].TrimEnd(':').Equals("outlook", StringComparison.OrdinalIgnoreCase)
                || words[^1].TrimEnd(':').Equals("guidance", StringComparison.OrdinalIgnoreCase));

    public readonly record struct Block(string Text, bool Emphasised);

    // The exhibit as a sequence of blocks with their own text.
    //
    // Innermost only, because an outer division holds every block inside it and a
    // reader taking both finds the whole document as one heading. The filings are
    // inline XBRL and carry no heading tags at all: emphasis is a weight in a
    // style attribute on a font element inside the block.
    public static List<Block> Blocks(string markup)
    {
        var stripped = Regex.Replace(markup, "<(script|style)[^>]*>.*?</\\1>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var blocks = new List<Block>();

        foreach (Match block in Innermost.Matches(stripped))
        {
            var inner = block.Groups[3].Value;
            var text = Plain(inner);

            if (text.Length == 0)
            {
                continue;
            }

            blocks.Add(new Block(text, Emphasised(block.Groups[1].Value, block.Groups[2].Value + inner)));
        }

        return blocks;
    }

    // A block whose content opens no block of its own, which is what makes the
    // match the innermost one. Written as a lookahead inside the content rather
    // than as a match followed by a test: a pattern that swallowed an outer
    // division would consume the block inside it, and the test would never see
    // the one carrying the text.
    static readonly Regex Innermost = new(
        "<(div|p|h[1-6])\\b([^>]*)>((?:(?!<(?:div|p|h[1-6]|table)\\b)[\\s\\S])*?)</\\1>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static bool Emphasised(string tag, string markup) =>
        tag[0] is 'h' or 'H'
            || Regex.IsMatch(markup, "font-weight\\s*:\\s*(700|800|900|bold)", RegexOptions.IgnoreCase)
            || Regex.IsMatch(markup, "<(b|strong)\\b", RegexOptions.IgnoreCase);

    // ---- the route ------------------------------------------------------------

    static async Task<(FiledGuidance? Guidance, FiledDocument? Transcript)> GuidanceAsync(
        ArchiveFetch fetch,
        string padded,
        IReadOnlyList<IndexedFiling> results,
        List<string> notCarried,
        CancellationToken cancellation)
    {
        if (results.Count == 0)
        {
            notCarried.Add(Guidance);

            return (null, null);
        }

        var announcement = results.OrderByDescending(filing => filing.FilingDate).First();
        var page = await fetch(
            Request(ArchiveDocument.FilingIndex, padded, announcement.Accession, announcement.Accession + "-index.htm"),
            cancellation).ConfigureAwait(false);

        if (page is null)
        {
            notCarried.Add(Guidance);

            return (null, null);
        }

        var documents = Documents(page);
        var transcript = Transcript(documents);
        var release = Release(documents);

        if (release is null)
        {
            notCarried.Add(Guidance);

            return (null, transcript);
        }

        var exhibit = await fetch(
            Request(ArchiveDocument.ReleaseExhibit, padded, announcement.Accession, release.FileName),
            cancellation).ConfigureAwait(false);

        if (exhibit is null)
        {
            notCarried.Add(Guidance);

            return (null, transcript);
        }

        return (Locate(exhibit, release.FileName, announcement.FilingDate), transcript);
    }

    static async Task<(SegmentBreakdown? Segments, int Read)> SegmentsAsync(
        ArchiveFetch fetch,
        string padded,
        IReadOnlyList<IndexedFiling> periodic,
        List<string> notCarried,
        CancellationToken cancellation)
    {
        if (periodic.Count == 0)
        {
            notCarried.Add(Segments);

            return (null, 0);
        }

        // The most recent periodic filing, which is not always a quarter. A filer
        // whose newest one is the annual report has a segment table of three
        // twelve-month columns and no quarter in it, which is what the live
        // demonstration at 6.2 found on the third of three filers. Taking an older
        // quarterly filing instead would be older information for a shorter period,
        // and what makes either safe is that the table states the period it covers
        // and the store and the screen both carry it: a figure that says which span
        // it is over cannot be read as the other one.
        var filing = periodic.OrderByDescending(one => one.FilingDate).First();
        var list = await fetch(
            Request(ArchiveDocument.ReportList, padded, filing.Accession, ReportListFile),
            cancellation).ConfigureAwait(false);

        if (list is null)
        {
            notCarried.Add(Segments);

            return (null, 0);
        }

        var read = 0;

        foreach (var candidate in SegmentCandidates(list).Take(SegmentReportsAtMost))
        {
            var page = await fetch(
                Request(ArchiveDocument.SegmentReport, padded, filing.Accession, candidate),
                cancellation).ConfigureAwait(false);

            read++;

            if (page is not null && Breakdown(page, candidate) is { } breakdown)
            {
                return (breakdown, read);
            }
        }

        notCarried.Add(Segments);

        return (null, read);
    }

    static async Task<IReadOnlyList<ArchiveFact>> FactsAsync(
        ArchiveFetch fetch,
        string padded,
        List<string> notCarried,
        CancellationToken cancellation)
    {
        var payload = await fetch(Request(ArchiveDocument.CompanyFacts, padded), cancellation).ConfigureAwait(false);

        if (payload is null)
        {
            notCarried.Add(Facts);

            return [];
        }

        return Quarterly(FactsOf(payload));
    }

    // ---- reading the archive's own shapes -------------------------------------

    // The document out of the archive's SGML envelope, where it sent one.
    //
    // A filing's documents are stored wrapped and served as stored. The envelope's
    // own lines read as body text, and its type line carries the exhibit's type, so
    // a reader looking for an exhibit type in a document's text finds one.
    public static string Unwrapped(string body)
    {
        if (!body.TrimStart().StartsWith("<DOCUMENT>", StringComparison.OrdinalIgnoreCase))
        {
            return body;
        }

        var opens = body.IndexOf("<TEXT>", StringComparison.OrdinalIgnoreCase);

        if (opens < 0)
        {
            return body;
        }

        var closes = body.LastIndexOf("</TEXT>", StringComparison.OrdinalIgnoreCase);

        return closes > opens
            ? body[(opens + "<TEXT>".Length)..closes]
            : body[(opens + "<TEXT>".Length)..];
    }

    static IReadOnlyList<string> Rows(string markup) =>
    [
        .. Regex
            .Matches(markup, "<tr[^>]*>.*?</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline)
            .Select(row => row.Value),
    ];

    // A cell's own text: the markup taken out, the entities resolved, and the
    // non-breaking spaces the renderer fills empty cells with treated as empty.
    public static string Plain(string markup)
    {
        var text = System.Net.WebUtility.HtmlDecode(Regex.Replace(markup, "<[^>]+>", " "));

        return Regex.Replace(text.Replace(NonBreakingSpace, ' '), "\\s+", " ").Trim();
    }

    // What the renderer fills an empty cell with. Stated as its code point rather
    // than typed, because the two are one character apart and a source file shows
    // them the same.
    public const char NonBreakingSpace = '\u00a0';

    // A money figure as the renderer states it: a currency mark, separated
    // thousands, a negative in parentheses, and an empty cell as nothing rather
    // than as a zero.
    public static decimal? Figure(string cell, int scale = 1)
    {
        var text = cell.Trim();

        if (text.Length == 0)
        {
            return null;
        }

        var negative = text.StartsWith('(') && text.EndsWith(')');
        var digits = new string([.. text.Where(character => char.IsAsciiDigit(character) || character is '.' or '-')]);

        if (digits.Length == 0 || !decimal.TryParse(digits, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        if (negative)
        {
            value = -value;
        }

        return scale > 1 ? value * scale : value;
    }

    // A date as one of the archive's two renderings: the structured endpoints send
    // it as a year, a month and a day, and a rendered table writes it as a short
    // month name with a period after it.
    //
    // Neither is parsed against the machine's locale. The structured form is read
    // with an exact pattern and the invariant culture, and the rendered one is
    // resolved through a month table rather than by a name lookup, because a name
    // lookup is a locale read with nothing in it a grep would find.
    public static DateOnly? Day(string? value)
    {
        var text = (value ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            return null;
        }

        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso))
        {
            return iso;
        }

        var written = Written.Match(text);

        if (!written.Success)
        {
            return null;
        }

        var month = Array.FindIndex(Months, name => string.Equals(name, written.Groups[1].Value, StringComparison.OrdinalIgnoreCase));

        return month < 0
            ? null
            : new DateOnly(
                int.Parse(written.Groups[3].Value, CultureInfo.InvariantCulture),
                month + 1,
                int.Parse(written.Groups[2].Value, CultureInfo.InvariantCulture));
    }

    static readonly string[] Months =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    static readonly Regex Written = new("([A-Za-z]{3})\\.?\\s+([0-9]{1,2}),\\s*([0-9]{4})", RegexOptions.Compiled);

    // A header cell's date, which is the first thing in it.
    //
    // One filer's header cells carry the unit after the date, reading 'Jul. 31,
    // 2026 USD ($)' and in one column a second unit after that, so a reader
    // parsing the whole cell finds no date in any column.
    static DateOnly? Rendered(string cell) => Day(Plain(cell));
}
