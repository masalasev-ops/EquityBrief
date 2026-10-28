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

        // Written sections: the narrower margin withheld, the revenue miss given the stored beat beside it, DEPS spelled out.
        Assert.StartsWith("A sentence is withheld here: it said the operating margin was narrower", MarkRenderer.Readable(RopMarginSentence, facts), StringComparison.Ordinal);
        Assert.EndsWith("; the estimate the page draws puts the revenue at +0.2%, a beat.", MarkRenderer.Readable(RopRevenueSentence, facts), StringComparison.Ordinal);
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
}
