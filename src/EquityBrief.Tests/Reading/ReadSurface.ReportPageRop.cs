using EquityBrief.Api.Reading;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// ROP's page as it stood on the night of 2026-10-26, frozen: each case the operator's report page corrections named, its
// stored figures and written words copied here as the store held them, held to the rule that now answers it. A rule
// that moves fails here on the page that showed it first.
// see: Every report page rule is held over every name's drawn page and reported passed, failed or unexamined with the pages each failed on
public partial class ReadSurface
{
    const decimal RopClose = 441.65m;
    const decimal RopCompanyValue = 45_313_290_000m;

    static readonly DateOnly RopNight = new(2026, 10, 26);

    // The written sentences ROP's bear case and its risks drew, word for word.
    const string RopMarginSentence = "Selling, general and administrative costs grew faster than revenue and unallocated corporate costs also rose, leaving the operating margin slightly narrower than a year earlier [N].";
    const string RopRevenueSentence = "The bear case begins with revenue, which fell just short of the consensus estimate, so the quarter was an earnings beat without a demand beat [D2].";
    const string RopGuidanceSentence = "On guidance, Roper lifted its full year adjusted DEPS outlook in the newest release, raising both ends of the range it had set in the release before [D1][D2].";
    const string RopSells = "Roper Technologies sells vertical software and technology enabled products through market leading businesses that serve a variety of defensible niche markets, grouped into Application Software, Network Software and Technology Enabled Products [D1].";

    // The latest quarter's lines the page drew: revenue 0.2% over its estimate and 7.6% on a year, earnings a share 1.7%
    // over its estimate, and the operating margin 28.7% against 28.4% a year earlier.
    static EquityBrief.Core.Report.LatestQuarter RopQuarter()
    {
        static EquityBrief.Core.Report.QuarterLine Line(string name, double? against, double? year, double? margin = null, double? before = null) =>
            new(name, 1m, null, null, against, null, year, margin, before, false, null, null, null);

        return new(new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 22), new DateOnly(2025, 9, 30),
        [
            Line("Revenue", 0.2, 7.6),
            Line("Gross profit", null, 8.5, 70.1, 69.5),
            Line("Operating income", null, 8.8, 28.7, 28.4),
            Line("Net income", null, 21.6, 22.3, 19.8),
            Line("Earnings a share", 1.7, 14.6),
        ]);
    }

    [Fact]
    public void RopsFrozenPageHoldsEachCaseTheCorrectionsNamed()
    {
        var facts = EquityBrief.Core.Research.Directions.Of(RopQuarter());

        // Written sections: the narrower margin withheld, the revenue miss withheld with the two sources named in one
        // sentence, DEPS spelled out.
        Assert.StartsWith("A sentence is withheld here: it said the operating margin was narrower", MarkRenderer.Readable(RopMarginSentence, facts), StringComparison.Ordinal);
        Assert.EndsWith("its source counted the revenue a miss against its own consensus, where the stored figures put the revenue at +0.2% against the estimate the page draws, a beat.", MarkRenderer.Readable(RopRevenueSentence, facts), StringComparison.Ordinal);
        Assert.Contains("adjusted diluted earnings a share (DEPS) outlook", MarkRenderer.Readable(RopGuidanceSentence, facts), StringComparison.Ordinal);
        Assert.DoesNotContain("[D", MarkRenderer.Readable(RopGuidanceSentence, facts), StringComparison.Ordinal);

        // One number per concept: the provider's market value of 35,461,898,240, forward multiple of 14.9701 over 24.379 a
        // share and yield of 1.02% on a rate of 3.64, each struck far from the close of 441.65, are stale beside the
        // company's value of 45,313,290,000 and the forward multiple and yield worked at the close.
        var shares = RopCompanyValue / RopClose;

        Assert.True(EquityBrief.Core.Report.OneValue.StruckValue(35_461_898_240m, shares, RopClose)!.Stale);
        Assert.True(EquityBrief.Core.Report.OneValue.StruckMultiple(14.9701m, 24.379m, RopClose)!.Stale);
        Assert.True(EquityBrief.Core.Report.OneValue.StruckYield(0.0102m, 3.64m, RopClose)!.Stale);
        Assert.Equal("18.1", EquityBrief.Core.Report.OneValue.Multiple(RopClose, 24.379m)!.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("0.82%", EquityBrief.Core.Report.ConceptCheck.Yield(EquityBrief.Core.Report.OneValue.Yield(3.64m, RopClose)!.Value));

        // The trend gate's range said in plain words: its close and averages rose, its swing lows did not.
        Assert.EndsWith(
            "Here the newest swing low, 346.58 on 2026-09-29, is not above the one before it, 386.85 on 2026-08-13, so it reads a range.",
            EquityBrief.Core.Report.TrendWords.Why("range", RopClose, 387.90, 362.86, [new(new DateOnly(2026, 9, 29), 346.578m), new(new DateOnly(2026, 8, 13), 386.8467m)]),
            StringComparison.Ordinal);

        // News: Roche's stories, tagged with its own listing of the same letters, are none of Roper's.
        Assert.False(EquityBrief.Core.News.StoryMatch.IsAbout("Roche keeps 2026 outlook as Q3 sales rise 7% at constant currencies", "Basel. Roche said.", "ROP", "Roper Technologies Inc"));
        Assert.False(EquityBrief.Core.News.StoryMatch.IsAbout("What Could Roche Holding (SWX:ROP) Gain From FDA Priority Review In MS?", string.Empty, "ROP", "Roper Technologies Inc"));
        Assert.True(EquityBrief.Core.News.StoryMatch.IsAbout("Roper Technologies raises full year guidance", string.Empty, "ROP", "Roper Technologies Inc"));

        // A source naming a word of the industry's own name is relevant to its cycle, and one naming neither it nor the
        // company is not.
        Assert.True(EquityBrief.Core.Report.IndustryCheck.Relevant("A page on the industry", "https://www.semiconductors.org/a-page The scientific instruments industry text.", "Keysight Technologies Inc", "Scientific & Technical Instruments"));
        Assert.False(EquityBrief.Core.Report.IndustryCheck.Relevant("Bond markets this week", "https://www.example.org/bonds Yields moved.", "Keysight Technologies Inc", "Scientific & Technical Instruments"));

        // Industry: its own account of what it sells describes software, which the provider's group does not name.
        Assert.Equal("software", EquityBrief.Core.Report.IndustryCheck.Mismatch("Electronic Equipment, Instruments & Components", RopSells));
        Assert.Null(EquityBrief.Core.Report.IndustryCheck.Mismatch("Software", RopSells));

        // The next report, none on file, estimated from its report of 2026-01-27 a year on, after the drift's hold of 60.
        var estimate = EquityBrief.Core.Cards.CardLines.EstimatedReport([new DateOnly(2025, 10, 23), new DateOnly(2026, 1, 27), new DateOnly(2026, 4, 23), new DateOnly(2026, 7, 23), new DateOnly(2026, 10, 22)], RopNight)!.Value;

        Assert.Equal(new DateOnly(2027, 1, 26), estimate.Estimated);
        Assert.Equal("note", EquityBrief.Core.Cards.CardLines.EstimatedEarnings(estimate, RopNight, 60).Mark);

        // The management box opens on the newest release's sentence saying what the company reported, read from the
        // release's stored text as ROP's of 2026-10-22 begins: its headline's present tense is passed over for the sentence
        // its dateline opens.
        var opening = EquityBrief.Core.Report.ReleaseOpening.Of(
            "a2026q3earningsrelease.htm",
            new DateOnly(2026, 10, 22),
            "https://www.sec.gov/Archives/edgar/data/882835/000088283526000042/a2026q3earningsrelease.htm",
            "EX-99.1 2 a2026q3earningsrelease.htm EX-99.1 Document Roper Technologies announces third quarter financial results Increases full year DEPS guidance Sarasota, Florida, October 22, 2026 ... Roper Technologies, Inc. (Nasdaq: ROP) reported financial results for the third quarter ended September 30, 2026. Third quarter 2026 highlights",
            "Roper Technologies",
            "ROP");

        Assert.Equal("Roper Technologies, Inc. (Nasdaq: ROP) reported financial results for the third quarter ended September 30, 2026.", opening!.Line);
        Assert.Null(EquityBrief.Core.Report.ReleaseOpening.Of("x.htm", new DateOnly(2026, 10, 22), "u", "Roper Technologies will hold its call on Thursday, October 22, 2026 at 8:00 a.m.", "Roper Technologies", "ROP"));

        // The numbers' awaited line moves on once the quarter is reported: ROP's quarter to 2026-09-30, reported on
        // 2026-10-22 and not yet filed, while the readings read the quarter to 2026-06-30.
        var reported = EquityBrief.Core.Quarters.NumbersSay.Reported(new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 22), new DateOnly(2026, 6, 30));

        Assert.Equal("The quarter to 2026-09-30 was reported on 2026-10-22 in its results release and its filing is not stored yet, so these read the quarter to 2026-06-30.", reported);
        Assert.Equal("reported", EquityBrief.Core.Quarters.NumbersSay.PatternOf(reported));

        // The year's high the page draws and the high a breakout reads are two windows: 471.98 over the stored year to the
        // night, 478.74 over the sessions before it.
        var page = "<dd data-concept=\"years-high\" data-concept-value=\"471.98\">471.98</dd>";

        Assert.Empty(EquityBrief.Core.Report.ConceptCheck.Clashes(page + "<td>478.74, made 251 sessions before the night</td>"));
    }

    // ROP's release of 2026-10-22, its segment tables as the stored text flattens them, word for word: three months to
    // 2026-09-30 and 2025-09-30, then nine months, in millions.
    const string RopReleaseTables =
        "Roper Technologies, Inc. Selected Segment Financial Data (unaudited) (Amounts in millions; percentages of net revenues) Three months ended September 30, Nine months ended September 30, 2026 2025 2026 2025 Amount % Amount % Amount % Amount % "
        + "Net revenues: Application Software $ 1,239.7 $ 1,161.0 $ 3,612.0 $ 3,324.1 Network Software 446.1 413.4 1,304.6 1,174.7 Technology Enabled Products 486.0 443.1 1,459.4 1,345.1 Total $ 2,171.8 $ 2,017.5 $ 6,376.0 $ 5,843.9 "
        + "Gross profit: Application Software $ 865.5 69.8% $ 801.0 69.0% $ 2,511.8 69.5% $ 2,275.1 68.4% Network Software 378.0 84.7% 346.9 83.9% 1,101.8 84.5% 983.3 83.7% Technology Enabled Products 279.1 57.4% 255.1 57.6% 833.0 57.1% 783.7 58.3% Total $ 1,522.6 70.1% $ 1,403.0 69.5% $ 4,446.6 69.7% $ 4,042.1 69.2% "
        + "Operating profit*: Application Software $ 357.6 28.8% $ 323.0 27.8% $ 1,000.8 27.7% $ 894.4 26.9% Network Software 189.6 42.5% 178.1 43.1% 540.0 41.4% 514.1 43.8% Technology Enabled Products 160.7 33.1% 150.2 33.9% 480.8 32.9% 467.9 34.8% Total $ 707.9 32.6% $ 651.3 32.3% $ 2,021.6 31.7% $ 1,876.4 32.1% "
        + "* Segment operating profit is before unallocated corporate general and administrative expenses and enterprise-wide stock-based compensation.";

    const decimal RopQuarterRevenue = 2_171_800_000m;

    // ROP's stored revenue for the quarter to 2025-09-30, which the release's second column totals.
    const decimal RopYearEarlierRevenue = 2_017_500_000m;

    [Fact]
    public void RopsReleaseSegmentTableIsReadAtTheScaleItsTotalIsTheQuartersRevenueAndCarriedAsFacts()
    {
        var quarter = new DateOnly(2026, 9, 30);
        var read = EquityBrief.Core.Report.ReleaseSegments.Read(RopReleaseTables, quarter, RopQuarterRevenue, RopYearEarlierRevenue)!;

        // Worked by hand: 2,171.8 in millions is the quarter's reported revenue, and the three segments sum to it.
        Assert.Equal("the results release", read.Report);
        Assert.Equal(quarter, read.Quarter);
        Assert.Equal(RopQuarterRevenue, read.Total);
        Assert.Equal(RopQuarterRevenue, read.Sum);
        Assert.Equal(["Application Software", "Network Software", "Technology Enabled Products"], read.Rows.Select(row => row.Segment));

        // Application Software: 1,239.7 on 1,161.0 is 6.8% up, its profit of 357.6 on 323.0 is 10.7% up, and it is 57.1%
        // of 2,171.8; the gross profit table's figures between are read past.
        var software = read.Rows[0];

        Assert.Equal(1_239_700_000m, software.Revenue);
        Assert.Equal(1_161_000_000m, software.RevenueYearEarlier);
        Assert.Equal(357_600_000m, software.Earnings);
        Assert.Equal(323_000_000m, software.EarningsYearEarlier);
        Assert.Equal([6.8, 10.7, 57.1], [Math.Round(software.RevenueChange!.Value, 1), Math.Round(software.EarningsChange!.Value, 1), Math.Round(software.Share, 1)]);
        Assert.Equal([7.9, 6.5, 20.5], [Math.Round(read.Rows[1].RevenueChange!.Value, 1), Math.Round(read.Rows[1].EarningsChange!.Value, 1), Math.Round(read.Rows[1].Share, 1)]);
        Assert.Equal([9.7, 7.0, 22.4], [Math.Round(read.Rows[2].RevenueChange!.Value, 1), Math.Round(read.Rows[2].EarningsChange!.Value, 1), Math.Round(read.Rows[2].Share, 1)]);

        // No table is read where its total is no scale of the quarter's revenue, its segments miss its total by more than
        // their rounding, two of its segments share a name, or no revenue is reported.
        Assert.Null(EquityBrief.Core.Report.ReleaseSegments.Read(RopReleaseTables, quarter, 2_500_000_000m));
        Assert.Null(EquityBrief.Core.Report.ReleaseSegments.Read(RopReleaseTables.Replace("486.0 443.1", "476.0 443.1", StringComparison.Ordinal), quarter, RopQuarterRevenue));
        Assert.Null(EquityBrief.Core.Report.ReleaseSegments.Read(RopReleaseTables.Replace("Network Software 446.1", "Application Software 446.1", StringComparison.Ordinal), quarter, RopQuarterRevenue));
        Assert.Null(EquityBrief.Core.Report.ReleaseSegments.Read(RopReleaseTables, quarter, null));

        // The second column is read as the same quarter a year earlier only where its total is that quarter's stored revenue
        // within a per cent: none is read with no stored revenue for it, or against one it misses.
        var unmatched = EquityBrief.Core.Report.ReleaseSegments.Read(RopReleaseTables, quarter, RopQuarterRevenue)!;

        Assert.All(unmatched.Rows, row => Assert.Equal((null, null, null, null), (row.RevenueYearEarlier, row.RevenueChange, row.EarningsYearEarlier, row.EarningsChange)));
        Assert.All(EquityBrief.Core.Report.ReleaseSegments.Read(RopReleaseTables, quarter, RopQuarterRevenue, 2_100_000_000m)!.Rows, row => Assert.Null(row.RevenueYearEarlier));
        Assert.Equal(357_600_000m, unmatched.Rows[0].Earnings);

        // The facts file carries the table as a filing's segment figures are named, from the results release: the company's
        // total and each segment's revenue and operating profit for the quarter and the same quarter a year earlier.
        var facts = EquityBrief.Worker.Facts.FactsAssembler.ReleaseSegmentFacts(read);

        Assert.Equal(13, facts.Count);
        Assert.All(facts, fact => Assert.Equal("results release", fact.Source));
        Assert.Contains(new EquityBrief.Core.Facts.Fact("segment total Net revenues 2026-09-30", "2171800000", "results release"), facts);
        Assert.Contains(new EquityBrief.Core.Facts.Fact("segment Network Software Net revenues 2025-09-30", "413400000", "results release"), facts);
        Assert.Contains(new EquityBrief.Core.Facts.Fact("segment Technology Enabled Products Operating profit 2026-09-30", "160700000", "results release"), facts);
        Assert.Equal(facts.Count, facts.Select(fact => fact.Name).Distinct(StringComparer.Ordinal).Count());
    }

    // Two other layouts the S&P 500's releases of October 2026 use, word for word: HON's, its heading followed by its
    // columns' years rather than a colon and its segments closing on a total of their own, and GPC's, closing on the next
    // heading with no total at all, read against its segments' own sum.
    // The dash HON's release stands in an empty cell, built from its code point since no file here carries the character.
    static readonly string EmDash = char.ConvertFromUtf32(0x2014);

    static readonly string HonReleaseTables =
        "Segment Data (Unaudited) (Dollars in millions) Three Months Ended September 30, Nine Months Ended September 30, Net sales 2026 2025 2026 2025 Building Automation $ 2,020 $ 1,878 $ 5,904 $ 5,396 Process Automation and Technology 1,859 1,598 5,051 4,656 Industrial Automation 1,115 1,450 4,039 4,627 Total Segment sales 4,994 4,926 14,994 14,679 Quantinuum " + EmDash + " 3 10 24 Total Net sales $ 4,994 $ 4,929 $ 15,004 $ 14,703 "
        + "Reconciliation of Segment Profit to Income Before Taxes Three Months Ended September 30, Nine Months Ended September 30, Segment profit 2026 2025 2026 2025 Building Automation $ 546 $ 502 $ 1,584 $ 1,421 Process Automation and Technology 412 389 1,142 1,088 Industrial Automation 205 216 703 704 Corporate and All Other (101) (241) (438) (650) Total Segment profit 1,062 866 2,991 2,563";

    const string GpcReleaseTables =
        "Three Months Ended September 30, Nine Months Ended September 30, (in thousands) 2026 2025 2026 2025 Net sales: North America Automotive $ 2,540,319 $ 2,484,591 $ 7,440,587 $ 7,193,749 International Automotive 1,572,392 1,505,197 4,746,020 4,373,208 Industrial 2,408,597 2,270,444 7,136,592 6,723,769 Segment EBITDA: North America Automotive 193,860 199,626 558,393 543,121";

    // HON's table: three segments summing to its 4,994 million, its profit read from the table its heading's years open,
    // and no year-earlier column, its 4,926 million missing the 10,408 million stored for the quarter a year before, which
    // the company reported before it split. GPC's: 6,521,308 thousand summed, its year-earlier column 6,260,232 thousand,
    // the stored 6,260,232,000, so North America Automotive is 2.2% up on 2,484,591.
    // see: A newer quarter's segments are read from its results release's own tables until its filing is stored, labelled so
    [Fact]
    public void AReleasesSegmentsAreReadUnderAHeadingOfYearsOrWithNoTotalAndAYearEarlierColumnOnlyWhereItIsThatQuartersRevenue()
    {
        var quarter = new DateOnly(2026, 9, 30);
        var hon = EquityBrief.Core.Report.ReleaseSegments.Read(HonReleaseTables, quarter, 4_994_000_000m, 10_408_000_000m)!;

        Assert.Equal(["Building Automation", "Process Automation and Technology", "Industrial Automation"], hon.Rows.Select(row => row.Segment));
        Assert.Equal([2_020_000_000m, 1_859_000_000m, 1_115_000_000m], hon.Rows.Select(row => row.Revenue));
        Assert.Equal([546_000_000m, 412_000_000m, 205_000_000m], hon.Rows.Select(row => row.Earnings!.Value));
        Assert.All(hon.Rows, row => Assert.Null(row.RevenueYearEarlier));
        Assert.Equal(4_994_000_000m, hon.Total);

        var gpc = EquityBrief.Core.Report.ReleaseSegments.Read(GpcReleaseTables, quarter, 6_521_308_000m, 6_260_232_000m)!;

        Assert.Equal(6_521_308_000m, gpc.Total);
        Assert.Equal(2_484_591_000m, gpc.Rows[0].RevenueYearEarlier);
        Assert.Equal(2.2, Math.Round(gpc.Rows[0].RevenueChange!.Value, 1));
        Assert.All(gpc.Rows, row => Assert.Null(row.Earnings));
        Assert.Null(EquityBrief.Core.Report.ReleaseSegments.Read(GpcReleaseTables, quarter, 6_600_000_000m, 6_260_232_000m));
    }

    // A segment commentary is read as written from the release's own table only where the facts file it was written from,
    // the newest the store computed for the name by the day it was written, carried the release's figures: ROP's of
    // 2026-10-27 was written from the night of 2026-10-26's, which carried the filing's quarter to June.
    // see: A newer quarter's segments are read from its results release's own tables until its filing is stored, labelled so
    [Fact]
    public async Task ASegmentCommentaryIsReadAsTheReleasesOnlyWhereItsFactsFileCarriedTheReleasesTable()
    {
        using var store = new TemporaryStore().Migrated();

        var filed = EquityBrief.Core.Facts.FactsFile.Serialise("ROP", new DateOnly(2026, 10, 26), [new("segment Application Software Net revenues 2026-06-30", "1181000000", "fundamental")]);
        var released = EquityBrief.Core.Facts.FactsFile.Serialise(
            "ROP",
            new DateOnly(2026, 10, 28),
            EquityBrief.Worker.Facts.FactsAssembler.ReleaseSegmentFacts(EquityBrief.Core.Report.ReleaseSegments.Read(RopReleaseTables, new DateOnly(2026, 9, 30), RopQuarterRevenue)!));

        Insert(store, "INSERT INTO facts (ticker, session_date, payload, payload_hash) VALUES ('ROP', '2026-10-26', '" + filed.Replace("'", "''", StringComparison.Ordinal) + "', 'filed');");
        Insert(store, "INSERT INTO facts (ticker, session_date, payload, payload_hash) VALUES ('ROP', '2026-10-28', '" + released.Replace("'", "''", StringComparison.Ordinal) + "', 'released');");

        var api = Api(store);

        Assert.False(await api.WrittenFromReleaseSegmentsAsync("ROP", new DateOnly(2026, 10, 27)));
        Assert.True(await api.WrittenFromReleaseSegmentsAsync("ROP", new DateOnly(2026, 10, 28)));
        Assert.False(await api.WrittenFromReleaseSegmentsAsync("ROP", new DateOnly(2026, 10, 25)));
    }
}
