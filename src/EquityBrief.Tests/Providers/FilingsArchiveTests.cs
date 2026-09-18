using EquityBrief.Core.Providers;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Providers;

// The filings archive, and the ten things the captures settled that the endpoint
// names and the corpus's own description of the archive would each have got wrong.
//
// The reader was written against thirteen captured responses from four filers and
// not the other way round, which is the rule 1.6 and 1.7 set after 1.2 stored a
// membership parser reading a field the provider does not send: the fixture agreed
// with it for two checkpoints because one session wrote both.
//
// One test per finding. A single test over one filer would pass on the parts that
// happen to be uniform and say nothing about the filer whose markup differs, which
// is why two filers are captured whole and two more for one cell each.
public class FilingsArchiveTests
{
    static string Folder() => Path.Combine(Repository.Root, "fixtures", "membership-2026-09-05");

    static string Captured(string file) => File.ReadAllText(Path.Combine(Folder(), file));

    static RecordedFilingsArchiveFeed Recorded() => new(Folder());

    // The two filers whose whole route is captured, with the CIK the company
    // financials payload carries for each.
    public const string AppleCik = "0000320193";

    public const string KeysightCik = "0001601046";

    [Fact]
    public void TheFilingIndexIsParallelArraysAndNeverAListOfFilings()
    {
        // The first thing a reader gets wrong. `filings.recent` is one array per
        // field, so a reader taking it for a list of objects finds none at all and
        // reports a company with no filings.
        var filings = SecEdgarArchive.Filings(Captured("filings-AAPL.json"));

        Assert.Equal(14, filings.Count);

        // Every position carries every field, which is what the parallel shape
        // means: the form at a position and the date at the same position are one
        // filing.
        Assert.All(filings, filing =>
        {
            Assert.NotEqual(string.Empty, filing.Form);
            Assert.NotEqual(string.Empty, filing.Accession);
            Assert.True(filing.FilingDate > new DateOnly(2025, 1, 1));
        });

        var quarter = filings.Single(filing => filing.Accession == "0000320193-26-000020");

        Assert.Equal("10-Q", quarter.Form);
        Assert.Equal(new DateOnly(2026, 7, 31), quarter.FilingDate);
        Assert.Equal(new DateOnly(2026, 6, 27), quarter.PeriodEnd);
    }

    [Fact]
    public void AResultsAnnouncementIsAnEightKCarryingItemTwoOhTwo()
    {
        // The only thing in the index that says the earnings release is attached to
        // a filing. The captured window holds eight 8-K filings across the two
        // filers and four of them announce results.
        var apple = SecEdgarArchive.Filings(Captured("filings-AAPL.json"));
        var keysight = SecEdgarArchive.Filings(Captured("filings-KEYS.json"));

        var announcing = apple.Concat(keysight).Where(SecEdgarArchive.Announces).ToArray();

        Assert.Equal(4, announcing.Length);
        Assert.All(announcing, filing => Assert.Equal("8-K", filing.Form));
        Assert.All(announcing, filing => Assert.Contains("2.02", filing.Items));

        // And the item is a whole item rather than a substring, so a filing whose
        // items were 2.021 or 12.02 would not answer for one that is 2.02.
        Assert.False(SecEdgarArchive.Announces(Filed("2.021,9.01")));
        Assert.False(SecEdgarArchive.Announces(Filed("12.02")));
        Assert.True(SecEdgarArchive.Announces(Filed("9.01,2.02")));
    }

    static IndexedFiling Filed(string items) => new(
        "8-K", "0000000000-26-000001", new DateOnly(2026, 8, 1), null,
        items.Split(','), "x.htm");

    [Fact]
    public void TheDirectoryListingSaysNothingAboutWhatAnyDocumentIs()
    {
        // The capture that is here for what it does not carry. Its `type` field is
        // a display icon, so a reader taking a document kind out of the directory
        // listing reads the name of an image.
        var listing = Captured("filing-index-AAPL-10q.json");

        Assert.Contains("text.gif", listing, StringComparison.Ordinal);
        Assert.DoesNotContain("EX-99", listing, StringComparison.OrdinalIgnoreCase);

        // And the page that does carry it does, which is the pair that makes the
        // route fetch a page a reader would not think to ask for.
        Assert.Contains("EX-99", Captured("filing-types-AAPL-8k.htm"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheReleaseIsTheExhibitTypeAndNotItsLongerSpelling()
    {
        // Two of the four captured filers type the release `EX-99.1` and two type
        // it `EX-99` with no suffix, so a reader keyed on the longer spelling finds
        // no release for a sixth of the index and reports that none was filed.
        var apple = SecEdgarArchive.Documents(Captured("filing-types-AAPL-8k.htm"));
        var honeywell = SecEdgarArchive.Documents(Captured("filing-types-HON-8k.htm"));

        Assert.Equal("EX-99.1", SecEdgarArchive.Release(apple)!.Type);
        Assert.Equal("EX-99", SecEdgarArchive.Release(honeywell)!.Type);

        Assert.Equal("a8-kex991q3202606272026.htm", SecEdgarArchive.Release(apple)!.FileName);
        Assert.Equal("exhibit99-q22026earningsre.htm", SecEdgarArchive.Release(honeywell)!.FileName);
    }

    [Fact]
    public void WhereAFilingCarriesTwoExhibitsTheReleaseIsTheLowerSequence()
    {
        // The rule that picks between two matches, which taking the only match
        // would have got right until a filer filed two.
        var documents = SecEdgarArchive.Documents(Captured("filing-types-CAT-8k.htm"));
        var exhibits = documents.Where(document => document.Type.StartsWith("EX-99", StringComparison.Ordinal)).ToArray();

        Assert.Equal(2, exhibits.Length);
        Assert.Equal([2, 3], exhibits.Select(exhibit => exhibit.Sequence).Order());

        var release = SecEdgarArchive.Release(documents)!;

        Assert.Equal(2, release.Sequence);
        Assert.Equal("EX-99.1", release.Type);
    }

    [Fact]
    public void TheWholeSubmissionIsNeverOfferedAsADocument()
    {
        // The last row of the index page is the whole filing as one file and its
        // sequence cell is a non-breaking space, so a reader taking every row
        // offers a four hundred kilobyte submission as an exhibit.
        var documents = SecEdgarArchive.Documents(Captured("filing-types-AAPL-8k.htm"));

        Assert.DoesNotContain(documents, document => document.FileName.EndsWith(".txt", StringComparison.Ordinal));
        Assert.All(documents, document => Assert.True(document.Sequence > 0));

        // The data-file table is not read at all. Its taxonomy documents are typed
        // EX-101.SCH and the like, so a reader taking both tables finds exhibits
        // that are not exhibits.
        Assert.DoesNotContain(documents, document => document.Type.StartsWith("EX-101", StringComparison.Ordinal));
    }

    [Fact]
    public void ADocumentComesOutOfTheArchivesOwnEnvelope()
    {
        // A filing's documents are stored wrapped and served as stored. The
        // envelope's own lines read as body text and its type line carries the
        // exhibit's type, so a reader looking for a type in a document's text finds
        // one where the document says nothing.
        var wrapped = Captured("release-AAPL.htm");

        Assert.StartsWith("<DOCUMENT>", wrapped, StringComparison.Ordinal);
        Assert.Contains("<TYPE>EX-99.1", wrapped, StringComparison.Ordinal);

        var unwrapped = SecEdgarArchive.Unwrapped(wrapped);

        Assert.DoesNotContain("<DOCUMENT>", unwrapped, StringComparison.Ordinal);
        Assert.DoesNotContain("<TYPE>EX-99.1", unwrapped, StringComparison.Ordinal);
        Assert.StartsWith("<html>", unwrapped.TrimStart(), StringComparison.OrdinalIgnoreCase);

        // And a document that arrives without one is left exactly as it came. The
        // index page is served bare, so unwrapping is a thing done where the
        // archive did it and never a thing assumed.
        var bare = Captured("filing-types-AAPL-8k.htm");

        Assert.Equal(bare, SecEdgarArchive.Unwrapped(bare));
    }

    [Fact]
    public void WhichSegmentReportHoldsFiguresIsReadFromTheTableAndNotFromItsName()
    {
        // The finding that decides the route. One filing lists 47 rendered reports
        // and four mention segments; the other lists 89 and six do. Narrowing to
        // the Details category leaves two and four, and which of those holds
        // figures is a property of the table.
        var apple = SecEdgarArchive.SegmentCandidates(Captured("report-list-AAPL-10q.xml"));
        var keysight = SecEdgarArchive.SegmentCandidates(Captured("report-list-KEYS-10q.xml"));

        Assert.Equal(["R45.htm", "R46.htm"], apple);
        Assert.Equal(["R85.htm", "R86.htm", "R87.htm", "R88.htm"], keysight);

        // The first candidate of one filing is the narrative one and carries a
        // single boolean, and the first of the other carries the figures. So a
        // route taking the first by name is wrong for one filer and right for the
        // other, which is why both are captured.
        Assert.Null(SecEdgarArchive.Breakdown(Captured("segment-report-AAPL-R45.htm"), "R45.htm"));
        Assert.NotNull(SecEdgarArchive.Breakdown(Captured("segment-report-AAPL-R46.htm"), "R46.htm"));
        Assert.NotNull(SecEdgarArchive.Breakdown(Captured("segment-report-KEYS-R85.htm"), "R85.htm"));
    }

    [Fact]
    public void TheTablesScaleIsStatedOnceAndAppliedToEveryMoneyRow()
    {
        // The figures are in millions and the scale is stated in the title cell
        // alone, so a reader taking them at face value is out by a factor of a
        // million on every one.
        var table = SecEdgarArchive.Breakdown(Captured("segment-report-AAPL-R46.htm"), "R46.htm")!;

        Assert.Contains("in Millions", table.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1_000_000, table.Scale);

        var sales = table.Consolidated.First(figure =>
            figure.Concept.EndsWith("RevenueFromContractWithCustomerExcludingAssessedTax", StringComparison.Ordinal)
            && figure.Period.Months == 3);

        // 109,417 as rendered, which is the quarter's net sales in dollars.
        Assert.Equal(109_417_000_000m, sales.Value);

        // A negative is parenthesised rather than signed.
        var cost = table.Consolidated.First(figure =>
            figure.Concept.EndsWith("CostOfGoodsAndServicesSold", StringComparison.Ordinal)
            && figure.Period.Months == 3);

        Assert.Equal(-54_647_000_000m, cost.Value);
    }

    [Fact]
    public void ARowStatingItsOwnUnitIsNotInTheTablesScale()
    {
        // One row of a money table is not money: a count of reportable segments,
        // marked by a unit after its label. A reader applying the table's scale
        // records two million segments.
        var table = SecEdgarArchive.Breakdown(Captured("segment-report-KEYS-R85.htm"), "R85.htm")!;

        Assert.Equal(1_000_000, table.Scale);

        var count = table.Consolidated.Single(figure =>
            figure.Concept.EndsWith("NumberOfReportableSegments", StringComparison.Ordinal)
            && figure.Value is not null);

        Assert.Equal("segment", count.Unit);
        Assert.Equal(2m, count.Value);
        Assert.Equal("Number of Reportable Segments", count.LineItem);

        // And the money rows of the same table do carry the scale, so the
        // exemption is a row's and not the table's.
        var revenue = table.Consolidated.First(figure =>
            figure.LineItem == "Revenues, Total" && figure.Period.Months == 3);

        Assert.Null(revenue.Unit);
        Assert.Equal(1_846_000_000m, revenue.Value);
    }

    [Fact]
    public void ARowMarkedWithTheTablesOwnCurrencyIsInTheTablesScale()
    {
        // A third filer's table, in thousands, marks every row after its label: its counts
        // with "segment" and its money rows with the table's own currency. A reader that left
        // every marked row as filed stored NFLX's quarter's revenue a thousand times short,
        // which 6.11's production run found in its facts file.
        var table = SecEdgarArchive.Breakdown(Captured("segment-report-NFLX-R65.htm"), "R65.htm")!;

        Assert.Equal(1_000, table.Scale);
        Assert.Equal("$", SecEdgarArchive.Currency(table.Title));

        var revenue = table.Consolidated.First(figure => figure.LineItem == "Revenues" && figure.Period.Months == 3);

        // $ 12,559,938 as rendered, which is the quarter's revenue in dollars.
        Assert.Equal("$", revenue.Unit);
        Assert.Equal(12_559_938_000m, revenue.Value);

        var unitedStates = Assert.Single(table.Groups, group => group.Label == "United States")
            .Figures.First(figure => figure.Period.Months == 3);

        Assert.Equal(5_100_000_000m, unitedStates.Value);

        // And the counts beside them are still counts.
        var count = table.Consolidated.First(figure => figure.LineItem == "Number of operating segments" && figure.Value is not null);

        Assert.Equal("segment", count.Unit);
        Assert.Equal(1m, count.Value);

        // A title stating no scale states no currency.
        Assert.Null(SecEdgarArchive.Currency("Segment Information (Details)"));
    }

    [Fact]
    public void ARowWrittenInPercentagesIsAShareAndNotInTheTablesScale()
    {
        // A narrative table in millions carries a customer concentration share
        // written as percentages, with no unit after its label. A reader applying
        // the title's scale to every such row stores 38 percent as 38 million.
        var page = Captured("segment-report-NVDA-R62.htm");
        var table = SecEdgarArchive.Breakdown(page, "R62.htm")!;

        Assert.Equal(1_000_000, table.Scale);

        var rendered = new[] { "38.00%", "30.00%", "35.00%" };

        Assert.All(rendered, cell => Assert.Contains(">" + cell + "<", page, StringComparison.Ordinal));

        var shares = table.Groups
            .SelectMany(group => group.Figures)
            .Where(figure => figure.LineItem == "Concentration risk (as percent)")
            .ToArray();

        Assert.Equal([38m, 30m, 30m, 35m], shares.Select(figure => figure.Value));
        Assert.All(shares, figure => Assert.Equal(SecEdgarArchive.Percent, figure.Unit));

        // The money rows of the same table keep the scale, so the exemption is the
        // row's and not the table's.
        var depreciation = table.Groups
            .SelectMany(group => group.Figures)
            .Where(figure => figure.LineItem == "Depreciation and amortization" && figure.Value is not null)
            .ToArray();

        Assert.NotEmpty(depreciation);
        Assert.All(depreciation, figure => Assert.Null(figure.Unit));
        Assert.Contains(depreciation, figure => figure.Value == 642_000_000m);
    }

    [Fact]
    public void APeriodColumnBelongsToTheHeaderWhoseSpanReachesIt()
    {
        // One end date under two spans. A reader keyed on the date alone takes the
        // three-month figure and the nine-month figure as the same quarter, which
        // is the difference between a quarter's revenue and three quarters of it.
        var table = SecEdgarArchive.Breakdown(Captured("segment-report-AAPL-R46.htm"), "R46.htm")!;

        Assert.Equal(4, table.Periods.Count);
        Assert.Equal([3, 3, 9, 9], table.Periods.Select(period => period.Months));
        Assert.Equal(
            [new DateOnly(2026, 6, 27), new DateOnly(2025, 6, 28), new DateOnly(2026, 6, 27), new DateOnly(2025, 6, 28)],
            table.Periods.Select(period => period.Ended));

        // The same end date on two columns, with a different span, which is what
        // makes the pair the key rather than the date.
        var ended = table.Periods.Where(period => period.Ended == new DateOnly(2026, 6, 27)).ToArray();

        Assert.Equal(2, ended.Length);
        Assert.Equal([3, 9], ended.Select(period => period.Months).Order());
    }

    [Fact]
    public void AHeaderCellsDateIsReadPastTheUnitAfterIt()
    {
        // One filer's header cells carry the unit after the date, reading
        // 'Jul. 31, 2026 USD ($)' and in one column a second unit after that, so a
        // reader parsing the whole cell finds no date in any column.
        var table = SecEdgarArchive.Breakdown(Captured("segment-report-KEYS-R85.htm"), "R85.htm")!;

        Assert.Contains("USD ($)", Captured("segment-report-KEYS-R85.htm"), StringComparison.Ordinal);
        Assert.Contains("USD ($) segment", SecEdgarArchive.Plain(Captured("segment-report-KEYS-R85.htm")), StringComparison.Ordinal);

        Assert.Equal(4, table.Periods.Count);
        Assert.Equal([3, 3, 9, 9], table.Periods.Select(period => period.Months));
        Assert.All(table.Periods, period => Assert.Equal(31, period.Ended.Day));
        Assert.All(table.Periods, period => Assert.Equal(7, period.Ended.Month));
    }

    [Fact]
    public void TwoGroupsSharingAMemberAreTwoGroupsAndNeverOne()
    {
        // Neither the label nor the concept identifies a group. Four groups of one
        // captured table share one axis and member in the markup while their labels
        // differ, and two labels appear twice, so a reader keyed on either takes
        // whichever section came last.
        var table = SecEdgarArchive.Breakdown(Captured("segment-report-KEYS-R85.htm"), "R85.htm")!;

        Assert.Equal(9, table.Groups.Count);

        var dimensions = table.Groups.Select(group => group.Dimension).Distinct(StringComparer.Ordinal).ToArray();

        Assert.Equal(4, dimensions.Length);

        var repeated = table.Groups
            .GroupBy(group => group.Label, StringComparer.Ordinal)
            .Where(same => same.Count() > 1)
            .ToArray();

        Assert.Equal(2, repeated.Length);
        Assert.All(repeated, same => Assert.Equal(2, same.Count()));

        // And the groups are a sequence in the order the table states them, which
        // is the only faithful reading of a table whose keys repeat.
        Assert.Equal(
            table.Groups.Select(group => group.Label),
            table.Groups.Select(group => group.Label));
    }

    [Fact]
    public void TheRowsAboveTheFirstGroupingAreTheCompanyAndNotASegment()
    {
        // The consolidated figures sit above the first grouping with no label
        // saying so, and a reader that put them in the first group would report the
        // whole company's revenue as one segment's.
        var table = SecEdgarArchive.Breakdown(Captured("segment-report-AAPL-R46.htm"), "R46.htm")!;

        Assert.Equal(6, table.Groups.Count);
        Assert.NotEmpty(table.Consolidated);

        var consolidated = table.Consolidated
            .First(figure => figure.LineItem == "Net sales" && figure.Period.Months == 3);

        var americas = table.Groups
            .Single(group => group.Label.StartsWith("Americas", StringComparison.Ordinal))
            .Figures
            .First(figure => figure.LineItem == "Net sales" && figure.Period.Months == 3);

        // The whole is larger than the largest part, which is the arithmetic that
        // says the split landed the right way round.
        Assert.True(consolidated.Value > americas.Value);
        Assert.Equal(45_781_000_000m, americas.Value);
    }

    [Fact]
    public void AStructuralRowCarriesNothingAndAnEmptyCellIsNotAZero()
    {
        // The renderer's own rows: a bracketed label with every cell a
        // non-breaking space. A reader taking them as figures records a company
        // whose segment reporting line items are zero.
        var table = SecEdgarArchive.Breakdown(Captured("segment-report-KEYS-R85.htm"), "R85.htm")!;

        var figures = table.Consolidated.Concat(table.Groups.SelectMany(group => group.Figures)).ToArray();

        Assert.DoesNotContain(figures, figure => figure.LineItem.EndsWith("]", StringComparison.Ordinal));

        // An empty cell inside a row that does carry figures is null rather than
        // zero, because a segment the filer stated nothing for is not a segment
        // that earned nothing.
        var blank = table.Groups
            .First(group => group.Label == "Communications Solutions Group")
            .Figures
            .Where(figure => figure.LineItem == "Revenues, Total")
            .ToArray();

        Assert.Equal(4, blank.Length);
        Assert.Contains(blank, figure => figure.Value is null);
        Assert.Contains(blank, figure => figure.Value == 3_700_000_000m);
    }

    [Fact]
    public void GuidanceIsLocatedByAHeadingAndNeverByTheWordsInTheProse()
    {
        // The words are everywhere. One captured exhibit uses outlook four times,
        // in a subtitle, in a quotation, in the heading and in a sentence about a
        // webcast, and the word guidance appears in it once, inside the
        // forward-looking-statements boilerplate. A reader keyed on either word
        // extracts the legal disclaimer.
        var exhibit = Captured("release-KEYS.htm");
        var plain = SecEdgarArchive.Plain(SecEdgarArchive.Unwrapped(exhibit));

        Assert.Equal(5, Occurrences(plain, "outlook"));
        Assert.Equal(1, Occurrences(plain, "guidance"));

        var guidance = SecEdgarArchive.Locate(exhibit, "exhibit991-q326pressrelease.htm", new DateOnly(2026, 8, 18));

        Assert.True(guidance.Located);
        Assert.Equal("Outlook", guidance.Heading);
        Assert.StartsWith("Keysight", guidance.Passage, StringComparison.Ordinal);
        Assert.Contains("$1.930 billion to $1.950 billion", guidance.Passage, StringComparison.Ordinal);

        // It stops at the next heading rather than running to the end of the
        // document, so the passage is management's forecast and not the whole
        // release.
        Assert.DoesNotContain("Webcast", guidance.Passage, StringComparison.Ordinal);
        Assert.DoesNotContain("Forward-Looking Statements", guidance.Passage, StringComparison.Ordinal);
    }

    [Fact]
    public void AnExhibitWithNoHeadingIsNotACompanyThatGaveNoGuidance()
    {
        // The absence case, and the distinction the measurement forced. Over twelve
        // filers, five state guidance under a heading and at least two of the other
        // seven state it inside an ordinary paragraph or a quotation, so a heading
        // locates it for fewer than half. This filer's exhibit states none at all:
        // the words guidance, outlook and expects appear zero times in it.
        var exhibit = Captured("release-AAPL.htm");
        var plain = SecEdgarArchive.Plain(SecEdgarArchive.Unwrapped(exhibit));

        Assert.Equal(0, Occurrences(plain, "outlook"));
        Assert.Equal(0, Occurrences(plain, "guidance"));
        Assert.Equal(0, Occurrences(plain, "expect"));

        var guidance = SecEdgarArchive.Locate(exhibit, "a8-kex991q3202606272026.htm", new DateOnly(2026, 7, 30));

        // The exhibit is named and dated either way, because a passage nobody
        // located and a company that gave no guidance are different things and the
        // document is the evidence for which it was.
        Assert.False(guidance.Located);
        Assert.Null(guidance.Heading);
        Assert.Null(guidance.Passage);
        Assert.Equal("a8-kex991q3202606272026.htm", guidance.Document);
        Assert.Equal(new DateOnly(2026, 7, 30), guidance.FiledOn);
    }

    [Fact]
    public void AHeadingIsAShortEmphasisedBlockEndingInTheWordAndNotTheWordAnywhere()
    {
        // The heading test in both directions over the spellings the measurement
        // found: five of twelve filers carry one and they write it 'Outlook',
        // 'Business Outlook', 'Guidance', '2026 Outlook' and as a fiscal year and
        // the word. The subtitle of one captured exhibit is emphasised, carries the
        // word, and is eleven words ending in 'improved', so both halves of the
        // test do work.
        var blocks = SecEdgarArchive.Blocks(
            "<div style=\"font-weight:700\">Second consecutive record quarter with orders over $2 billion; "
            + "full-year outlook improved</div>"
            + "<div style=\"font-weight:700\">2026 Outlook</div>"
            + "<div>a sentence about the fourth quarter outlook that is not a heading at all</div>"
            + "<div><b>Guidance:</b></div>"
            + "<div style=\"font-weight:400\">Business Outlook</div>");

        Assert.Equal(5, blocks.Count);
        Assert.Equal([true, true, false, true, false], blocks.Select(block => block.Emphasised));

        var located = SecEdgarArchive.Locate(
            "<div style=\"font-weight:700\">Second consecutive record quarter with orders over $2 billion; "
            + "full-year outlook improved</div>"
            + "<div>the release's opening paragraph, which mentions the outlook and is not it</div>"
            + "<div style=\"font-weight:700\">2026 Outlook</div>"
            + "<div>revenue is expected to be in the range of one figure to another</div>"
            + "<div style=\"font-weight:700\">Webcast</div>"
            + "<div>a sentence about the call</div>",
            "constructed.htm",
            new DateOnly(2026, 8, 1));

        Assert.Equal("2026 Outlook", located.Heading);
        Assert.Equal("revenue is expected to be in the range of one figure to another", located.Passage);
    }

    [Fact]
    public void NoneOfTheCapturedFilersFilesATranscriptAndNoneIsInvented()
    {
        // Measured rather than assumed: none of the twelve filers probed at 6.2
        // filed a transcript, and the four whose index pages are captured file
        // none. So the ordinary answer is none, and an empty one would read as a
        // transcript that was filed and could not be read.
        // see: Transcripts are opportunistic, never a dependency
        foreach (var page in new[] { "filing-types-AAPL-8k.htm", "filing-types-KEYS-8k.htm", "filing-types-HON-8k.htm", "filing-types-CAT-8k.htm" })
        {
            Assert.Null(SecEdgarArchive.Transcript(SecEdgarArchive.Documents(Captured(page))));
        }

        // And the reading finds one where a filing indexes one, so the absence is
        // the archive's and not the reader's. Constructed, because the archive held
        // none to capture.
        var withOne = SecEdgarArchive.Documents(
            "<table summary=\"Document Format Files\">"
            + "<tr><td>1</td><td>8-K</td><td><a href=\"/Archives/x/y/a.htm\">a.htm</a></td><td>8-K</td><td>1</td></tr>"
            + "<tr><td>2</td><td>EX-99.1</td><td><a href=\"/Archives/x/y/b.htm\">b.htm</a></td><td>EX-99.1</td><td>1</td></tr>"
            + "<tr><td>3</td><td>Earnings call transcript</td><td><a href=\"/Archives/x/y/c.htm\">c.htm</a></td><td>EX-99.2</td><td>1</td></tr>"
            + "</table>");

        Assert.Equal("c.htm", SecEdgarArchive.Transcript(withOne)!.FileName);

        // And the transcript is never taken for the release, which is what would
        // happen if the release were the only exhibit match.
        Assert.Equal("b.htm", SecEdgarArchive.Release(withOne)!.FileName);
    }

    [Fact]
    public void TheCikIsPaddedBecauseTheTwoEndpointsDisagreeAboutIt()
    {
        // One endpoint sends it as a zero-padded string and the other as an
        // unpadded number, so the two would address two different companies if
        // either were used as it arrived.
        Assert.Equal(AppleCik, SecEdgarArchive.CikOf(Captured("filings-AAPL.json")));
        Assert.Equal(AppleCik, SecEdgarArchive.CikOf(Captured("company-facts-AAPL.json")));

        Assert.Contains("\"cik\": \"0000320193\"", Captured("filings-AAPL.json"), StringComparison.Ordinal);
        Assert.Contains("\"cik\": 320193", Captured("company-facts-AAPL.json"), StringComparison.Ordinal);

        // And a filing's own path spells it without the padding, which is a third
        // spelling of one identifier.
        Assert.Equal("320193", SecEdgarArchive.Bare(AppleCik));
        Assert.Equal("1601046", SecEdgarArchive.Bare(KeysightCik));
    }

    [Fact]
    public void ARevenueConceptIsNeverTakenForTheRevenue()
    {
        // The archive files revenue under several concepts at once and does not
        // keep them all current. Over both captured payloads whole, one carries
        // `Revenues` to 2018 in 11 facts and its contract revenue to 2026 in 117,
        // and the other is the reverse, with 118 and 14. Both retired
        // `SalesRevenueNet` in 2018.
        var apple = SecEdgarArchive.FactsOf(Captured("company-facts-AAPL.json"));
        var keysight = SecEdgarArchive.FactsOf(Captured("company-facts-KEYS.json"));

        foreach (var facts in new[] { apple, keysight })
        {
            Assert.Contains(facts, fact => fact.Concept == "Revenues");
            Assert.Contains(facts, fact => fact.Concept == "RevenueFromContractWithCustomerExcludingAssessedTax");
            Assert.Contains(facts, fact => fact.Concept == "SalesRevenueNet");
        }

        // The staleness is in the fixture rather than only in the reasoning: one
        // filer's `Revenues` stops seven years before the other's.
        Assert.Equal(2018, apple.Where(fact => fact.Concept == "Revenues").Max(fact => fact.End.Year));
        Assert.Equal(2026, keysight.Where(fact => fact.Concept == "Revenues").Max(fact => fact.End.Year));
        Assert.Equal(2026, apple.Where(fact => fact.Concept == "RevenueFromContractWithCustomerExcludingAssessedTax").Max(fact => fact.End.Year));

        // So every fact keeps the concept it was filed against, and nothing here
        // resolves one of them to be the revenue.
        Assert.All(apple, fact => Assert.NotEqual(string.Empty, fact.Concept));
    }

    [Fact]
    public void AQuarterlyFactIsTheOneTheArchiveFramesAndTheLastFilingOfIt()
    {
        // Two readings the payload forces. A fiscal period label sits on a
        // three-month figure and on the nine months that contain it, and the frame
        // the archive assigns names a calendar quarter and only ever sits on the
        // shorter one. And one period appears twice under two accession numbers
        // where a filing restated it.
        var facts = SecEdgarArchive.FactsOf(Captured("company-facts-AAPL.json"));
        var income = facts.Where(fact => fact.Concept == "NetIncomeLoss").Where(fact => fact.Start is not null).ToArray();

        var cumulative = income.Where(fact => (fact.End.DayNumber - fact.Start!.Value.DayNumber) > 180).ToArray();

        Assert.NotEmpty(cumulative);

        // Not that a cumulative fact has no frame: the year has one, reading
        // 'CY2025'. What holds is that no span longer than a quarter carries a
        // calendar-quarter frame, which is the form the quarterly reading keys on
        // and the reason it keys on the form rather than on the field's presence.
        Assert.All(cumulative, fact => Assert.DoesNotContain("Q", fact.Frame ?? string.Empty, StringComparison.Ordinal));
        Assert.Contains(cumulative, fact => fact.Frame == "CY2025");
        Assert.Contains(cumulative, fact => fact.Frame is null);

        var restated = income
            .GroupBy(fact => (fact.Start, fact.End))
            .Where(period => period.Count() > 1)
            .ToArray();

        Assert.NotEmpty(restated);
        Assert.All(restated, period => Assert.True(period.Select(fact => fact.Accession).Distinct().Count() > 1));

        // The quarterly reading takes the framed facts, one per period, at the
        // latest filing of each.
        var quarterly = SecEdgarArchive.Quarterly(facts).Where(fact => fact.Concept == "NetIncomeLoss").ToArray();

        Assert.All(quarterly, fact => Assert.True((fact.End.DayNumber - fact.Start!.Value.DayNumber) < 120));
        Assert.Equal(
            quarterly.Length,
            quarterly.Select(fact => (fact.Start, fact.End)).Distinct().Count());

        var june = quarterly.Single(fact => fact.End == new DateOnly(2025, 6, 28));

        Assert.Equal(new DateOnly(2026, 7, 31), june.Filed);
        Assert.Equal("CY2025Q2", june.Frame);
    }

    [Fact]
    public void AnInstantFactCarriesNoStartDateAndIsKeptAnyway()
    {
        // The finding the captures forced, and the one a reader would lose in
        // silence. Every balance-sheet figure is as of an instant: the archive sends
        // no start date for one at all and writes its frame with an I after the
        // quarter. A reader requiring both dates keeps revenue and earnings and drops
        // every asset and liability line while looking complete, and a reader
        // matching the span frame alone does the same one step later.
        var facts = SecEdgarArchive.FactsOf(Captured("company-facts-AAPL.json"));
        var assets = facts.Where(fact => fact.Concept == "Assets").ToArray();

        Assert.NotEmpty(assets);
        Assert.All(assets, fact => Assert.True(SecEdgarArchive.IsAnInstant(fact)));
        Assert.All(assets, fact => Assert.Null(fact.Start));

        // The archive says it twice and the two agree: no start date, and an I on
        // the frame wherever it assigns one.
        Assert.All(
            assets.Where(fact => fact.Frame is not null),
            fact => Assert.EndsWith("I", fact.Frame!, StringComparison.Ordinal));

        // And a span fact never carries the instant form, which is the other
        // direction of the same statement.
        Assert.All(
            facts.Where(fact => fact.Start is not null).Where(fact => fact.Frame is not null),
            fact => Assert.False(fact.Frame!.EndsWith("I", StringComparison.Ordinal)));

        // So the quarterly reading keeps both kinds. Five concepts over this filer's
        // capture, one of them an instant: a reading of four would be the defect.
        var quarterly = SecEdgarArchive.Quarterly(facts);

        Assert.Equal(SecEdgarArchive.Kept.Length, quarterly.Select(fact => fact.Concept).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(quarterly, fact => fact.Concept == "Assets");
        Assert.Equal(3, quarterly.Where(SecEdgarArchive.IsAnInstant).Select(fact => fact.Concept).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task OneReadWalksTheWholeRouteOverTheCapturesAndCountsWhatItFetched()
    {
        // The route end to end over one filer, which is what makes the captures a
        // replay rather than a set of documents a test opens one at a time.
        var feed = Recorded();
        var apple = await feed.FilingsAsync("AAPL", AppleCik);

        Assert.Equal(1, feed.Requests);
        Assert.Equal(AppleCik, apple.Cik);

        // Seven documents: the filing index, the announcement's index page, the
        // release exhibit, the report list, two report pages because the first
        // holds no figures, and the company facts.
        Assert.Equal(7, feed.Asked.Count);
        Assert.Equal(2, apple.SegmentReportsRead);

        Assert.Equal(
            [
                ArchiveDocument.Submissions,
                ArchiveDocument.FilingIndex,
                ArchiveDocument.ReleaseExhibit,
                ArchiveDocument.ReportList,
                ArchiveDocument.SegmentReport,
                ArchiveDocument.SegmentReport,
                ArchiveDocument.CompanyFacts,
            ],
            feed.Asked.Select(request => request.Document));

        Assert.NotNull(apple.Segments);
        Assert.Equal("R46.htm", apple.Segments.Report);
        Assert.NotNull(apple.Guidance);
        Assert.False(apple.Guidance.Located);
        Assert.Null(apple.Transcript);
        Assert.NotEmpty(apple.Facts);

        // The one part its filing does not serve: no segment report of it is named for
        // revenue, so the route asks for nothing past the segment table.
        Assert.Equal([SecEdgarArchive.RevenueTables], apple.PartsNotCarried);
        Assert.Empty(apple.RevenueTables!);
    }

    [Fact]
    public async Task TheSecondFilerNeedsOneReportPageWhereTheFirstNeedsTwo()
    {
        // The same route over the filer whose first segment candidate carries the
        // figures, so the count is a property of the filing and not a constant.
        var feed = Recorded();
        var keysight = await feed.FilingsAsync("KEYS", KeysightCik);

        Assert.Equal(6, feed.Asked.Count);
        Assert.Equal(1, keysight.SegmentReportsRead);
        Assert.Equal("R85.htm", keysight.Segments!.Report);

        // And this is the filer whose release carries guidance, which is the pair
        // the two captures exist for.
        Assert.True(keysight.Guidance!.Located);
        Assert.Equal("Outlook", keysight.Guidance.Heading);
    }

    // A filing index holding the one periodic filing a captured report list belongs
    // to, constructed because the segment half of the route asks the index for
    // nothing but that filing.
    const string NvidiaIndex = """
        {"cik": "1045810", "filings": {"recent": {
          "accessionNumber": ["0001045810-26-000075"],
          "filingDate": ["2026-08-26"],
          "reportDate": ["2026-07-26"],
          "form": ["10-Q"],
          "items": [""],
          "primaryDocument": ["nvda-20260726.htm"]}}}
        """;

    static ArchiveFetch Serving(Func<string, string?> report) =>
        (request, _) => Task.FromResult<string?>(request.Document switch
        {
            ArchiveDocument.Submissions => NvidiaIndex,
            ArchiveDocument.ReportList => Captured("report-list-NVDA-10q.xml"),
            ArchiveDocument.SegmentReport => report(Path.GetFileName(request.Path)),
            _ => null,
        });

    [Fact]
    public async Task TheTableTakenIsTheFirstStatingRevenueBySegmentAndNotTheFirstCarryingAFigure()
    {
        // A filing whose first segment candidate is a narrative table carrying
        // figures by segment that are not what the segments earned. A route taking
        // the first table with any grouped figure stores depreciation and a
        // concentration share as the segment table, and the revenue table after it
        // goes unread.
        Assert.Equal(
            ["R62.htm", "R63.htm", "R64.htm", "R65.htm", "R66.htm", "R67.htm"],
            SecEdgarArchive.SegmentCandidates(Captured("report-list-NVDA-10q.xml")));

        var narrative = SecEdgarArchive.Breakdown(Captured("segment-report-NVDA-R62.htm"), "R62.htm")!;
        var schedule = SecEdgarArchive.Breakdown(Captured("segment-report-NVDA-R63.htm"), "R63.htm")!;

        Assert.False(SecEdgarArchive.StatesRevenue(narrative));
        Assert.True(SecEdgarArchive.StatesRevenue(schedule));

        // Every other captured table the route takes states revenue by segment, so
        // the preference moves no other filer's choice.
        Assert.All(
            new[] { "segment-report-AAPL-R46.htm", "segment-report-KEYS-R85.htm", "segment-report-NFLX-R65.htm" },
            file => Assert.True(SecEdgarArchive.StatesRevenue(SecEdgarArchive.Breakdown(Captured(file), file)!)));

        // The route over the captured list and pages. Past the table it takes, the
        // route asks only for the reports named for revenue, and a page it has no
        // business with is never asked for, which the missing capture would refuse.
        var filings = await SecEdgarArchive.ReadAsync(
            Serving(report => Captured("segment-report-NVDA-" + report)),
            "NVDA",
            "0001045810");

        Assert.Equal("R63.htm", filings.Segments!.Report);
        Assert.Equal(4, filings.SegmentReportsRead);

        // 88,299 as rendered under '$ in Millions', the quarter's revenue of the
        // larger segment in dollars.
        var revenue = filings.Segments.Groups
            .Single(group => group.Label.EndsWith("Compute & Networking", StringComparison.Ordinal))
            .Figures
            .First(figure => figure.LineItem == "Revenue" && figure.Period.Months == 3);

        Assert.Equal(88_299_000_000m, revenue.Value);
        Assert.Equal(new DateOnly(2026, 7, 26), revenue.Period.Ended);

        // Where no candidate states revenue, the first carrying figures is still
        // taken, after the route has read as many pages as it may, and each revenue
        // report past those once.
        var fallback = await SecEdgarArchive.ReadAsync(
            Serving(report => report == "R62.htm" ? Captured("segment-report-NVDA-R62.htm") : null),
            "NVDA",
            "0001045810");

        Assert.NotNull(fallback.Segments);
        Assert.Equal("R62.htm", fallback.Segments.Report);
        Assert.Equal(SecEdgarArchive.SegmentReportsAtMost + 1, fallback.SegmentReportsRead);
        Assert.DoesNotContain(SecEdgarArchive.Segments, fallback.PartsNotCarried);
        Assert.Contains(SecEdgarArchive.RevenueTables, fallback.PartsNotCarried);
    }

    [Fact]
    public async Task TheFilingsOtherTablesOfRevenueByAGroupingAreKeptBesideItsSegmentTable()
    {
        // A filing states revenue by more than its segments: by market platform and by
        // region beside the two segments. The figures a release headlines are there, the
        // quarter's data center revenue among them, and the segment table alone holds
        // none of them.
        Assert.Equal(["R65.htm", "R67.htm"], SecEdgarArchive.RevenueCandidates(Captured("report-list-NVDA-10q.xml")));

        // Neither other captured filing names a segment report for revenue, so neither
        // read costs a further request.
        Assert.Empty(SecEdgarArchive.RevenueCandidates(Captured("report-list-AAPL-10q.xml")));
        Assert.Empty(SecEdgarArchive.RevenueCandidates(Captured("report-list-KEYS-10q.xml")));

        var filings = await SecEdgarArchive.ReadAsync(
            Serving(report => Captured("segment-report-NVDA-" + report)),
            "NVDA",
            "0001045810");

        Assert.Equal(["R65.htm", "R67.htm"], filings.RevenueTables!.Select(table => table.Report));
        Assert.DoesNotContain(SecEdgarArchive.RevenueTables, filings.PartsNotCarried);

        // The rendered cells, scaled by the title's millions: 89,023 for data center and
        // 26,985 for Taiwan, the quarter to 2026-07-26.
        decimal Quarter(string report, string group) => filings.RevenueTables!
            .Single(table => table.Report == report)
            .Groups.Single(held => held.Label == group)
            .Figures.First(figure => figure.LineItem == "Revenue" && figure.Period.Months == 3 && figure.Period.Ended == new DateOnly(2026, 7, 26))
            .Value!.Value;

        Assert.Contains(">89,023<", Captured("segment-report-NVDA-R67.htm"), StringComparison.Ordinal);
        Assert.Equal(89_023_000_000m, Quarter("R67.htm", "Data Center"));
        Assert.Equal(26_985_000_000m, Quarter("R65.htm", "Taiwan"));

        // A report named for revenue that states none by a grouping is read and not kept,
        // here the narrative table served under the region report's name.
        var misnamed = await SecEdgarArchive.ReadAsync(
            Serving(report => Captured("segment-report-NVDA-" + (report == "R65.htm" ? "R62.htm" : report))),
            "NVDA",
            "0001045810");

        Assert.Equal(["R67.htm"], misnamed.RevenueTables!.Select(table => table.Report));
    }

    [Fact]
    public async Task TheReleaseIsHandedOverAsADocumentWithTheAddressItWasReadAtItsFilingDateAndItsText()
    {
        // 6.8's reading of the route: the exhibit a research pass rests the company's
        // own claims on, as a document admissibility can test. The same exhibit the
        // guidance was located in, filed the same day, at the address the manifest
        // recorded for it, and its text rather than its markup.
        var manifest = Captured("manifest.json");

        foreach (var (ticker, cik) in new[] { ("KEYS", KeysightCik), ("AAPL", AppleCik) })
        {
            var filings = await Recorded().FilingsAsync(ticker, cik);
            var release = filings.Release;

            Assert.NotNull(release);
            Assert.Equal(filings.Guidance!.Document, release.Document);
            Assert.Equal(filings.Guidance.FiledOn, release.FiledOn);

            var prefix = "https://" + SecEdgarArchive.DocumentHost + "/";

            Assert.StartsWith(prefix, release.Url, StringComparison.Ordinal);
            Assert.EndsWith("/" + release.Document, release.Url, StringComparison.Ordinal);
            Assert.Contains("\"endpoint\": \"" + release.Url[prefix.Length..] + "\"", manifest, StringComparison.Ordinal);

            Assert.NotEmpty(release.Text);
            Assert.DoesNotContain("<td", release.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<p", release.Text, StringComparison.OrdinalIgnoreCase);
        }

        // The filer whose release carries guidance carries its heading in the text.
        var keysight = await Recorded().FilingsAsync("KEYS", KeysightCik);

        Assert.Equal(new DateOnly(2026, 8, 18), keysight.Release!.FiledOn);
        Assert.Contains(keysight.Guidance!.Heading!, keysight.Release.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheAddressesTheRouteComposesAreTheOnesTheManifestRecorded()
    {
        // The half a replay cannot show by itself. The recorded feed answers by
        // document kind, so the paths it composed are checked against the addresses
        // the manifest records for the same captures, which is where the live feed
        // would send them.
        var manifest = Captured("manifest.json");

        foreach (var (document, accession, file) in new[]
        {
            (ArchiveDocument.FilingIndex, "0000320193-26-000018", "0000320193-26-000018-index.htm"),
            (ArchiveDocument.ReportList, "0000320193-26-000020", "FilingSummary.xml"),
            (ArchiveDocument.SegmentReport, "0000320193-26-000020", "R46.htm"),
            (ArchiveDocument.ReleaseExhibit, "0000320193-26-000018", "a8-kex991q3202606272026.htm"),
        })
        {
            var request = SecEdgarArchive.Request(document, AppleCik, accession, file);

            Assert.Equal(SecEdgarArchive.DocumentHost, request.Host);
            Assert.Contains("\"endpoint\": \"" + request.Path + "\"", manifest, StringComparison.Ordinal);
        }

        foreach (var document in new[] { ArchiveDocument.Submissions, ArchiveDocument.CompanyFacts })
        {
            var request = SecEdgarArchive.Request(document, AppleCik);

            Assert.Equal(SecEdgarArchive.DataHost, request.Host);
            Assert.Contains("\"endpoint\": \"" + request.Path + "\"", manifest, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ANameWithNoCaptureRefusesRatherThanReadingAsAnAbsence()
    {
        // The replay's own refusal. A capture nobody committed is a fault in the
        // fixture, and answering it as nothing would read as the archive not
        // holding the document, which is the fault 6.1 found in the fundamentals
        // double's unknown-name path.
        var feed = Recorded();

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => feed.FilingsAsync("MSFT", "0000789019"));

        Assert.Contains("0 captured file(s)", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Submissions", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACaptureAnsweringTwoPrefixesRefusesRatherThanTakingTheFirst()
    {
        // Both directions on the count, because a prefix answers about everything
        // sharing it and the order a directory yields is not a rule.
        var folder = Directory.CreateTempSubdirectory("archive-prefix").FullName;

        try
        {
            File.WriteAllText(Path.Combine(folder, "filings-A.json"), "{}");
            File.WriteAllText(Path.Combine(folder, "filings-AB.json"), "{}");

            var feed = new RecordedFilingsArchiveFeed(folder);
            var request = SecEdgarArchive.Request(ArchiveDocument.Submissions, AppleCik);

            // One capture for the longer name, which the shorter one's prefix also
            // reaches.
            Assert.EndsWith("filings-AB.json", feed.Held(request, "AB"), StringComparison.Ordinal);

            var several = Assert.Throws<InvalidOperationException>(() => feed.Held(request, "A"));

            Assert.Contains("2 captured file(s)", several.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task APartTheArchiveDoesNotServeIsNamedRatherThanLeftOut()
    {
        // The live side's own case, which a replay over a complete set cannot
        // reach: the archive answering that a document is not there. A filing with
        // no rendered reports and a company with no filed facts are both ordinary,
        // and each leaves the numbers section an absence to mark rather than a
        // blank to draw.
        var served = new Dictionary<ArchiveDocument, string?>
        {
            [ArchiveDocument.Submissions] = Captured("filings-AAPL.json"),
            [ArchiveDocument.FilingIndex] = Captured("filing-types-AAPL-8k.htm"),
            [ArchiveDocument.ReleaseExhibit] = Captured("release-AAPL.htm"),
            [ArchiveDocument.ReportList] = null,
            [ArchiveDocument.CompanyFacts] = null,
        };

        var filings = await SecEdgarArchive.ReadAsync(
            (request, _) => Task.FromResult(served[request.Document]),
            "AAPL",
            AppleCik);

        Assert.Equal(["segments", "revenueTables", "facts"], filings.PartsNotCarried);
        Assert.Null(filings.Segments);
        Assert.Empty(filings.Facts);
        Assert.Equal(0, filings.SegmentReportsRead);

        // And the parts that were served are still there, so an absence in one is
        // not an absence in the read.
        Assert.NotNull(filings.Guidance);
    }

    [Fact]
    public async Task AReadWithNoIdentifierIsRefusedRatherThanSent()
    {
        // The archive is addressed by CIK and nothing else, so a read with none
        // would be a read of whatever the archive answered.
        var asked = 0;

        await Assert.ThrowsAsync<ProviderRefusal>(() => SecEdgarArchive.ReadAsync(
            (_, _) =>
            {
                asked++;

                return Task.FromResult<string?>("{}");
            },
            "AAPL",
            "   "));

        Assert.Equal(0, asked);
    }

    [Fact]
    public void AFeedWithNoAgentRefusesBeforeARequestIsComposed()
    {
        // The transport rule, which is this provider's whole credential path: the
        // archive refuses a request naming no user agent and its fair-access policy
        // asks that the agent carry contact details.
        // see: The archive declares a contact in its user agent, and a blank one refuses at startup
        Assert.Throws<InvalidOperationException>(() => new ArchiveAgent("   "));

        Assert.Throws<ArgumentNullException>(() =>
            new SecEdgarFilingsArchiveFeed(new System.Net.Http.HttpClient(), null!));
    }

    [Fact]
    public async Task AClientCarryingNoAgentRefusesInsteadOfReachingTheArchive()
    {
        // The half the constructor cannot cover. A client built by hand rather than
        // by `Live` can carry no header, and a request sent without one comes back
        // refused for a reason nothing in the run would name, so it is refused here
        // before it is sent.
        var handler = new Counting();
        var feed = new SecEdgarFilingsArchiveFeed(
            new System.Net.Http.HttpClient(handler), new ArchiveAgent("someone@example.test"));

        var refused = await Assert.ThrowsAsync<ProviderRefusal>(() => feed.FilingsAsync("AAPL", AppleCik));

        Assert.Contains("names no user agent", refused.Message, StringComparison.Ordinal);
        Assert.Equal(0, handler.Sent);

        // And `Live` puts it on, which is the direction that says the refusal is
        // about a client built wrong rather than about the rule being unsatisfiable.
        var proper = SecEdgarFilingsArchiveFeed.Live(new ArchiveAgent("someone@example.test"));

        Assert.NotNull(proper);
    }

    sealed class Counting : System.Net.Http.HttpMessageHandler
    {
        internal int Sent { get; private set; }

        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Sent++;

            return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new System.Net.Http.StringContent("{}"),
            });
        }
    }

    static int Occurrences(string text, string word)
    {
        var count = 0;

        for (var at = text.IndexOf(word, StringComparison.OrdinalIgnoreCase);
             at >= 0;
             at = text.IndexOf(word, at + 1, StringComparison.OrdinalIgnoreCase))
        {
            count++;
        }

        return count;
    }
}
