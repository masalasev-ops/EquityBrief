using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Checks;

// architecture-conformance, 18.5: phase 18's pair read off the plan and checked against the actual, every row the phase
// added reached once and passing and each checkpoint's count against the rows it landed with where every difference
// went; and the page's layout figure's key naming every part the figure draws, in the architecture and in the guide, its
// regions the ones section 4 names in its order.
public partial class ArchitectureConformance
{
    // The rows each part of phase 18 landed, in the plan's order, and the rows it took out: the 3.1 correction's chart
    // averages and the 16.3 correction's dividend steps less the 6.10 correction's overnight queue; none at 18.0; 18.1's
    // quote job and the name page's parts; 18.2's keeper and reader and the page's seven regions and nine marks; 18.3's
    // page history reader and the parts the page gains, less the twelve-month picture and the reactions' old place; 18.4's
    // limits, failures and the parts its rows are read as, less the dates and sources; and none at 18.5, whose figure is a
    // picture of claims placed elsewhere.
    static (string Checkpoint, string[] Added, string[] Taken)[] PhaseEighteenLanded =>
    [
        ("corrections", [.. NightlyRun.ChartAverageRows, .. FixtureExpectations.DividendStepClaims, .. Reading.ReadSurface.MonthlyWorryRows], [.. TakenOutAfterPhaseSeventeen.Take(7)]),
        ("18.0", [], []),
        ("18.1", [.. ComponentAccess.QuoteJobRows, .. SchemaColumns.QuoteStoreRows, .. FixtureExpectations.QuoteRows, .. Reading.ReadSurface.NamePageRows], []),
        ("18.2", [.. ComponentAccess.ReportComponentRows, .. SchemaColumns.ReportStoreRows, CheckReach.Key(NightlyRunSteps.Heading, NightlyRun.TreasuryStep), .. FixtureExpectations.ReportRows, .. Reading.ReadSurface.ReportPageRows], []),
        ("18.3",
            [
                .. ComponentAccess.PageHistoryRows,
                .. SchemaColumns.PageHistoryStoreRows,
                .. FixtureExpectations.HistoryRows,
                CheckReach.Key(Scope.FailureTable, "The page history reader cannot finish"),
                CheckReach.Key(Scope.FailureTable, "A gap in one name's series, typical earnings move and long view"),
                .. Reading.ReadSurface.HistoryPageRows,
            ],
            [CheckReach.Key("15.9 Name", "How it got here, the twelve-month picture"), CheckReach.Key("15.9 Name", "Earnings reactions, beside the earnings setups")]),
        ("18.4", [.. FixtureExpectations.WrittenLastRows, .. Reading.ReadSurface.WrittenLastPageRows], [CheckReach.Key("15.9 Name", "Dates and sources")]),
        ("18.5", [], []),
    ];

    // The rows the document gains after phase 18's report, named beside the pair and never counted in it, which phase 19's
    // own report counts: 19.1's part of the card's rule on the S&P 400's and 600's variant reading no market check, and
    // 19.2's store of every member's readings.
    internal static string[] AfterPhaseEighteen => [.. Reading.ReadSurface.MarketOffRows, CheckReach.Key(Scope.StoresTable, "Ledger readings")];

    // How many more rows each part of phase 18 landed than its count in the plan, a negative figure where it landed fewer,
    // and what it landed. The corrections, counted one, landed the chart averages' step, catalogue and matrix rows, store
    // and the chart's part, the dividend steps' row and the 17.10 correction's checklist item on the newest monthly run,
    // less the queue's catalogue and matrix rows, step, section 17's row, section 18's two and the run page's region, one
    // fewer: the queue retired four rows more than counted, the averages landed three more, the monthly item one more, and
    // the market events' 2027 rows landed as words in a row that stood. 18.0, none as counted. 18.1,
    // twenty: the quote job's catalogue and matrix rows, two stores, section 17's row and section 18's three, the name
    // page's parts with the headline as the two of its parts that stand from 18.1, the range bar, the run page's quote runs
    // and the interval, two more, the palette's kinds, accents and tables landing as words in section 15.6 and the job's
    // rows and the headline's second part beyond the count. 18.2, thirty-two: the keeper's and the reader's catalogue and
    // matrix rows, three stores, the Treasury's step, section 17's two rows, section 18's six, the page's seven regions and
    // the nine marks, eight more. 18.3, thirty-six: the reader's catalogue and matrix rows, two stores, section 17's row,
    // section 18's seven with the gap row's part, the page's two regions, four marks and the parts the level chart, the
    // chart, the plan, the reactions and the moves gain, less the twelve-month picture and the reactions' old place, twenty
    // more. 18.4, thirty: section 17's two rows, section 18's three, the headline's two parts it writes last and dates, and
    // the risks' two columns, the calendar, the sources and the calendar timeline read as their twenty-four parts, less the
    // dates and sources, eighteen more. 18.5, none: the layout figure and its key, a drawn figure placed as a picture of
    // claims placed elsewhere, two fewer.
    static readonly (string Checkpoint, int More)[] PhaseEighteenMore =
    [
        ("corrections", -1),
        ("18.0", 0),
        ("18.1", 2),
        ("18.2", 8),
        ("18.3", 20),
        ("18.4", 18),
        ("18.5", -2),
    ];

    [Fact]
    public void ThePhaseEighteenPairIsCheckedAgainstTheActualWithEveryClaimThatMovedNamed()
    {
        // The pair and each part's count read off the plan, so the figures checked are the ones it carries.
        const string pair = @"([\d,]+) claims and \1 PASS after 17\.10, [^;]*; about (\d+) more after the 3\.1, 6\.10 and 16\.3 corrections, [^.]*\. After 18\.5 about ([\d,]+), within ([\d,]+) to ([\d,]+): none at 18\.0, [^;]*; about (\d+) at 18\.1, [^;]*; about (\d+) at 18\.2, [^;]*; about (\d+) at 18\.3; about (\d+) at 18\.4; and about (\d+) at 18\.5\.";
        var stated = Regex.Match(Corpus.Read("docs/BUILD_PLAN.md"), pair.Replace(" ", @"\s+", StringComparison.Ordinal));

        Assert.True(stated.Success, "The plan states no pair for phase 18.");

        int Figure(int group) => int.Parse(stated.Groups[group].Value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture);

        var (before, predicted, low, high) = (Figure(1), Figure(3), Figure(4), Figure(5));
        int[] counted = [Figure(2), 0, Figure(6), Figure(7), Figure(8), Figure(9), Figure(10)];

        Assert.Equal(predicted, before + counted.Sum());

        // What each part landed: its count in the plan and the rows it landed beyond it.
        var landed = PhaseEighteenLanded;

        Assert.Equal(landed.Select(part => part.Checkpoint), PhaseEighteenMore.Select(part => part.Checkpoint));
        Assert.Equal(
            counted.Select((count, at) => count + PhaseEighteenMore[at].More),
            landed.Select(part => part.Added.Length - part.Taken.Length));

        // The rows the lists the rest of the harness reads name, the same rows: what phase 17's report names after it, less
        // what phase 18's own report names after it, and what it takes out.
        Assert.Equal(AfterPhaseSeventeen.Except(AfterPhaseEighteen, StringComparer.Ordinal).Order(StringComparer.Ordinal), landed.SelectMany(part => part.Added).Order(StringComparer.Ordinal));
        Assert.Equal(TakenOutAfterPhaseSeventeen.Order(StringComparer.Ordinal), landed.SelectMany(part => part.Taken).Order(StringComparer.Ordinal));

        var rows = landed.SelectMany(part => part.Added).ToArray();
        var actual = before + rows.Length - landed.Sum(part => part.Taken.Length);

        // The actual stands two above the range the plan stated, past the pair by the rows each part landed beyond its
        // count, named above, the 17.10 correction's checklist item among them.
        Assert.Equal(predicted + PhaseEighteenMore.Sum(part => part.More), actual);
        Assert.Equal(high + 2, actual);

        // Every row the phase added reached once and passing, and the report holding the phase's rows over what stood before
        // it and the rows after its report, none out of scope and none unexamined.
        var report = Report();
        var total = actual + AfterPhaseEighteen.Length;

        Assert.Equal(rows.Length, rows.Distinct(StringComparer.Ordinal).Count());
        Assert.All(rows, key => Assert.Equal(Verdict.Pass, Assert.Single(report.Claims, claim => CheckReach.Key(claim.Table, claim.Subject) == key).Verdict));
        Assert.All(AfterPhaseEighteen, key => Assert.Contains(report.Claims, claim => CheckReach.Key(claim.Table, claim.Subject) == key));
        Assert.Equal(
            (total, 0, 0, total),
            (report.Claims.Count, report.Count(Verdict.OutOfScope), report.Count(Verdict.Unexamined), report.Count(Verdict.Pass)));

        // Stated, so a claim added or lost without being named here moves this rather than the sum.
        Assert.Equal((1149, 1222, 1185, 1265, 1267), (before, predicted, low, high, actual));
    }

    // see: The name page answers a buyer's questions in the order a buyer asks them
    [Fact]
    public void TheLayoutFiguresKeyNamesEveryPartItDrawsAndItsRegionsAreSectionFoursInItsOrder()
    {
        // The architecture's figure 15.1 and the key beneath it, and the guide's same picture and the list beneath it.
        var architecture = Corpus.Read("docs/ARCHITECTURE.html");
        var guide = Corpus.Read("docs/HOW_IT_WORKS.html");

        var (drawn, keyed) = Parts(architecture, "Figure 15.1.", "<div class=\"key\">", "</div>");

        Assert.Equal(Enumerable.Range(1, drawn.Count).Select(part => part.ToString(CultureInfo.InvariantCulture)), drawn.Keys);
        Assert.Equal(drawn.Keys, keyed.Keys);
        Assert.All(keyed.Values, words => Assert.True(words.Length > 40, $"A key line says too little to explain a part: '{words}'."));

        var (pictured, listed) = Parts(guide, "A stock's page, its figures blanked out", "<ol class=\"parts\">", "</ol>");

        Assert.Equal(drawn.Keys, pictured.Keys);
        Assert.Equal(pictured.Keys, listed.Keys);

        // After the masthead, the headline and the contents, each part is a region section 4 names, by the name the part
        // draws in its order, and the guide draws the same names.
        static string Named(string part) =>
            WebUtility.HtmlDecode(Regex.Matches(part, "<text[^>]*font-weight=\"600\">([^<]*)</text>")
                .Select(text => text.Groups[1].Value)
                .First(text => !Regex.IsMatch(text, @"^\d+$")));

        Assert.Equal(Reading.ReadSurface.ReportRegions(architecture), drawn.Values.Skip(3).Select(Named));
        Assert.Equal(drawn.Values.Select(Named), pictured.Values.Select(Named));

        // Every figure drawn as a placeholder and never a night's: no digit in either figure's text but a part's own number
        // and the index's name.
        foreach (var figure in new[] { Figure(architecture, "Figure 15.1."), Figure(guide, "A stock's page, its figures blanked out") })
        {
            var words = Regex.Matches(figure, "<text[^>]*>([^<]*)</text>").Select(text => text.Groups[1].Value)
                .Where(text => !Regex.IsMatch(text, @"^\d+$"))
                .Select(text => text.Replace("S&amp;P 500", string.Empty, StringComparison.Ordinal));

            Assert.All(words, text => Assert.DoesNotMatch(@"\d", text));
        }
    }
}
