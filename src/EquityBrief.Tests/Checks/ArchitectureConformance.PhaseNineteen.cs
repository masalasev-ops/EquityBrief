using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Checks;

// architecture-conformance, 19.2: Tonight's top's figure's key names every part the figure draws, in the architecture and
// in the guide, the guide's picture numbering the same parts, neither figure's text holding any night's figure.
public partial class ArchitectureConformance
{
    [Fact]
    public void TheTonightTopFiguresKeyNamesEveryPartTheFigureDraws()
    {
        // The architecture's figure 15.3 and the key beneath it, and the guide's same picture and the list beneath it.
        var architecture = Corpus.Read("docs/ARCHITECTURE.html");
        var guide = Corpus.Read("docs/HOW_IT_WORKS.html");

        var (drawn, keyed) = Parts(architecture, "Figure 15.3.", "<div class=\"key\">", "</div>");

        // Eight parts from 22.6, which redrew the top as the page draws it in the reader's words.
        Assert.Equal(Enumerable.Range(1, 8).Select(part => part.ToString(CultureInfo.InvariantCulture)), drawn.Keys);
        Assert.Equal(drawn.Keys, keyed.Keys);
        Assert.All(keyed.Values, words => Assert.True(words.Length > 40, $"A key line says too little to explain a part: '{words}'."));

        var (pictured, listed) = Parts(guide, "Tonight's top, its figures blanked out", "<ol class=\"parts\">", "</ol>");

        Assert.Equal(drawn.Keys, pictured.Keys);
        Assert.Equal(pictured.Keys, listed.Keys);

        // Every figure drawn as a placeholder and never a night's: no digit in either figure's text but a part's own
        // number, the three indices' names, the 200-day average the market's line names and the column the risk of $1 heads.
        foreach (var figure in new[] { Figure(architecture, "Figure 15.3."), Figure(guide, "Tonight's top, its figures blanked out") })
        {
            var words = Regex.Matches(figure, "<text[^>]*>(.*?)</text>").Select(text => Regex.Replace(text.Groups[1].Value, "<[^>]+>", string.Empty))
                .Where(text => !Regex.IsMatch(text, @"^\d$"))
                .Select(text => Regex.Replace(text, @"S&amp;P (500|400|600)|200-day|\$1 to make", string.Empty));

            Assert.All(words, text => Assert.DoesNotMatch(@"\d", text));
        }
    }

    // 19.5: the universe's figure the same way, its key naming every part it draws in the architecture and in the guide.
    [Fact]
    public void TheUniverseFiguresKeyNamesEveryPartTheFigureDraws()
    {
        var architecture = Corpus.Read("docs/ARCHITECTURE.html");
        var guide = Corpus.Read("docs/HOW_IT_WORKS.html");

        var (drawn, keyed) = Parts(architecture, "Figure 15.4.", "<div class=\"key\">", "</div>");

        Assert.Equal(Enumerable.Range(1, 6).Select(part => part.ToString(CultureInfo.InvariantCulture)), drawn.Keys);
        Assert.Equal(drawn.Keys, keyed.Keys);
        Assert.All(keyed.Values, words => Assert.True(words.Length > 40, $"A key line says too little to explain a part: '{words}'."));

        var (pictured, listed) = Parts(guide, "The universe, its figures blanked out", "<ol class=\"parts\">", "</ol>");

        Assert.Equal(drawn.Keys, pictured.Keys);
        Assert.Equal(pictured.Keys, listed.Keys);

        foreach (var figure in new[] { Figure(architecture, "Figure 15.4."), Figure(guide, "The universe, its figures blanked out") })
        {
            var words = Regex.Matches(figure, "<text[^>]*>(.*?)</text>").Select(text => Regex.Replace(text.Groups[1].Value, "<[^>]+>", string.Empty))
                .Where(text => !Regex.IsMatch(text, @"^\d$"));

            Assert.All(words, text => Assert.DoesNotMatch(@"\d", text));
        }
    }

    // 19.7: the Loop page's figure the same way, its key naming every part it draws in the architecture and in the guide.
    [Fact]
    public void TheLoopFiguresKeyNamesEveryPartTheFigureDraws()
    {
        var architecture = Corpus.Read("docs/ARCHITECTURE.html");
        var guide = Corpus.Read("docs/HOW_IT_WORKS.html");

        var (drawn, keyed) = Parts(architecture, "Figure 15.5.", "<div class=\"key\">", "</div>");

        Assert.Equal(Enumerable.Range(1, 5).Select(part => part.ToString(CultureInfo.InvariantCulture)), drawn.Keys);
        Assert.Equal(drawn.Keys, keyed.Keys);
        Assert.All(keyed.Values, words => Assert.True(words.Length > 40, $"A key line says too little to explain a part: '{words}'."));

        var (pictured, listed) = Parts(guide, "The Loop page, its figures blanked out", "<ol class=\"parts\">", "</ol>");

        Assert.Equal(drawn.Keys, pictured.Keys);
        Assert.Equal(pictured.Keys, listed.Keys);

        foreach (var figure in new[] { Figure(architecture, "Figure 15.5."), Figure(guide, "The Loop page, its figures blanked out") })
        {
            var words = Regex.Matches(figure, "<text[^>]*>(.*?)</text>").Select(text => Regex.Replace(text.Groups[1].Value, "<[^>]+>", string.Empty))
                .Where(text => !Regex.IsMatch(text, @"^\d$"));

            Assert.All(words, text => Assert.DoesNotMatch(@"\d", text));
        }
    }

    // 19.8: paper trading's figure the same way, its key naming every part it draws in the architecture and in the guide.
    [Fact]
    public void ThePaperTradingFiguresKeyNamesEveryPartTheFigureDraws()
    {
        var architecture = Corpus.Read("docs/ARCHITECTURE.html");
        var guide = Corpus.Read("docs/HOW_IT_WORKS.html");

        var (drawn, keyed) = Parts(architecture, "Figure 15.6.", "<div class=\"key\">", "</div>");

        Assert.Equal(Enumerable.Range(1, 6).Select(part => part.ToString(CultureInfo.InvariantCulture)), drawn.Keys);
        Assert.Equal(drawn.Keys, keyed.Keys);
        Assert.All(keyed.Values, words => Assert.True(words.Length > 40, $"A key line says too little to explain a part: '{words}'."));

        var (pictured, listed) = Parts(guide, "Paper trading, its figures blanked out", "<ol class=\"parts\">", "</ol>");

        Assert.Equal(drawn.Keys, pictured.Keys);
        Assert.Equal(pictured.Keys, listed.Keys);

        foreach (var figure in new[] { Figure(architecture, "Figure 15.6."), Figure(guide, "Paper trading, its figures blanked out") })
        {
            var words = Regex.Matches(figure, "<text[^>]*>(.*?)</text>").Select(text => Regex.Replace(text.Groups[1].Value, "<[^>]+>", string.Empty))
                .Where(text => !Regex.IsMatch(text, @"^\d$"));

            Assert.All(words, text => Assert.DoesNotMatch(@"\d", text));
        }
    }

    // The rows each part of phase 19 landed, in the plan's order, and the rows it took out, each correction counted with
    // the checkpoint it corrects: none at 19.0; 19.1's part of the card's rule on the variant reading no market check;
    // 19.2's store of every member's readings, its marks, Tonight's top and the rows it moves to the Run page, less the
    // night header's parts and the reason totals on Tonight; 19.3's parts of the setup cards and the heavyweights card,
    // less the parts it retires; 19.4's two stores, section 18's rule the per-rule test cannot walk and paper trading's
    // rows with the trade log's, its correction's start and version row among them; 19.5's Universe rows, the Run page's
    // corrections and section 17's two, less the sector strip's parts; 19.6's queue row of the reports written by hand and
    // section 17's window, and its correction's Write now row; 19.7's rows of the remaining screens less the Run page's and
    // the Watch list's parts it retires; and 19.8's card of the stocks one gate short on the operator's addendum, its
    // figure a picture of claims placed elsewhere.
    static (string Checkpoint, string[] Added, string[] Taken)[] PhaseNineteenLanded =>
    [
        ("19.0", [], []),
        ("19.1", [.. Reading.ReadSurface.MarketOffRows], []),
        ("19.2", [CheckReach.Key(Scope.StoresTable, "Ledger readings"), .. Reading.ReadSurface.ColourAndTopRows, .. Reading.ReadSurface.MovedToTheRunPageRows], [.. TakenOutAfterPhaseEighteen.Take(5)]),
        ("19.3", [.. Reading.ReadSurface.SetupCardRows], [.. Reading.ReadSurface.RetiredCardRows]),
        ("19.4", [CheckReach.Key(Scope.StoresTable, "Rule tests"), CheckReach.Key(Scope.StoresTable, "Rule test years"), .. FixtureExpectations.RuleTestClaims, .. Reading.ReadSurface.PaperRows], []),
        ("19.5", [.. Reading.ReadSurface.UniverseRows], [.. Reading.ReadSurface.RetiredUniverseRows]),
        ("19.6", [.. Reading.ReadSurface.QueueDayRows, .. Reading.ReadSurface.WriteNowRows], []),
        ("19.7", [.. Reading.ReadSurface.RemainingScreensRows], [.. Reading.ReadSurface.RetiredRunRows]),
        ("19.8", [.. Reading.ReadSurface.CloseToBuyRows], []),
    ];

    // The rows the document gains and loses after phase 19's report, named beside the pair and never counted in it, which
    // 19.9's, 19.10's and 19.11's own entries name: 19.9's none, its three landing as words in rows that stood, 19.10's three
    // on the operator's rulings of 2026-10-15, the fill step's catalogue and matrix rows and its store, and 19.11's eleven on
    // the rulings of 2026-10-16, the chart page's ten and the Run page's time of every page, and 19.12's three on the same
    // rulings, which readings go with winners on the Ledger page, its store and section 17's row, and 19.13's seven on the
    // operator's ruling of the same day, the insider trades' catalogue and matrix rows, their two stores, section 17's row,
    // section 18's and 15.9's region, the chart page's group landing as words in its figures row that stood and section 4's
    // region outside the claims; and the 17.3 correction of 2026-10-16's three, the SEC tickers step's catalogue and
    // matrix rows and its store; and 20.1's nine, the local run's catalogue and matrix rows, the reading pins' store,
    // section 17's row, section 18's four and the Run page's local model region, and 20.2's six, the release fetch's
    // catalogue and matrix rows, its two stores, section 17's row and section 18's, and 20.3's seven, the accuracy
    // report's catalogue and matrix rows, the three stores of the releases' filers, readings and probes, section 17's row
    // and section 18's, and 20.4's two, the tone variant's card row in 15.7 and section 17's window row, and 20.5's two,
    // the local comparison's catalogue and matrix rows, and 20.9's six, the idea generator's catalogue and matrix rows,
    // its store, the Loop page's row and section 18's two, and 20.8's four, the usefulness store, the Ledger page's and the
    // Loop page's rows and section 17's window row, and 20.12's four, the chart page's graphs and their three marks, and
    // 20.13's eighteen, the analyst job's and the ratings job's catalogue and matrix rows, their three stores, section
    // 17's four rows, the fourth from its correction of 2026-10-23, section 18's four, the target range among the marks
    // and the chart page's firm actions and analysts by name, which phase 20's own pair counts once its report is written; and the seven parts paper trading's redesign draws in the
    // place of its table of every account; and 22.1's thirteen, the dip family's catalogue and matrix rows, its four
    // stores, section 17's row, section 18's four, its card on Tonight and its trades on Past picks, and 22.2's three,
    // section 17's grid of the dip's sweep, section 18's row for a market series it reads missing a session and the
    // Ledger page's dip counts, and 22.4's one, the chart page's brief read, and 22.6's two, Tonight in one minute and the
    // page's words at its foot, and 22.8's nine, the fundamentals scorecard's catalogue and matrix rows, its store, section
    // 17's numbers, section 18's three and its card on the chart page and the full page, which phase 22's own pair counts
    // once its report is written.
    internal static string[] AfterPhaseNineteen =>
    [
        .. ComponentAccess.ScorerRows,
        .. SchemaColumns.ScoreStoreRows,
        .. FixtureExpectations.ScorecardClaims,
        .. Reading.ReadSurface.FundamentalsRows,
        .. ComponentAccess.DipRows,
        .. SchemaColumns.DipStoreRows,
        .. FixtureExpectations.DipClaims,
        .. Reading.ReadSurface.DipPageRows,
        .. FixtureExpectations.DipLedgerClaims,
        .. Reading.ReadSurface.DipLedgerPageRows,
        .. Reading.ReadSurface.BriefRows,
        .. Reading.ReadSurface.TonightRows,
        .. FixtureExpectations.TradeFillRows,
        .. Reading.ReadSurface.ChartRows,
        .. Reading.ReadSurface.ReadingLinkRows,
        CheckReach.Key(Harness.Scope.StoresTable, "Reading links"),
        .. FixtureExpectations.ReadingLinkClaims,
        .. ComponentAccess.InsiderTradeRows,
        .. SchemaColumns.InsiderStoreRows,
        .. FixtureExpectations.InsiderClaims,
        .. Reading.ReadSurface.InsiderRows,
        .. ComponentAccess.SecTickerRows,
        .. SchemaColumns.SecTickerStoreRows,
        .. ComponentAccess.LocalRunRows,
        .. SchemaColumns.ReadingPinStoreRows,
        .. FixtureExpectations.LocalRunClaims,
        .. Reading.ReadSurface.LocalModelRows,
        .. ComponentAccess.ReleaseFetchRows,
        .. SchemaColumns.ReleaseStoreRows,
        .. FixtureExpectations.ReleaseFetchClaims,
        .. ComponentAccess.ReleaseAccuracyRows,
        .. SchemaColumns.ReleaseReadingStoreRows,
        .. FixtureExpectations.ReleaseReadingClaims,
        .. FixtureExpectations.ToneVariantClaims,
        .. ComponentAccess.LocalComparisonRows,
        .. ComponentAccess.IdeaGeneratorRows,
        .. SchemaColumns.LoopIdeaStoreRows,
        .. Reading.ReadSurface.LoopIdeaClaims,
        .. FixtureExpectations.IdeaGeneratorClaims,
        .. SchemaColumns.ReleaseAnswerStoreRows,
        .. Reading.ReadSurface.ReleaseAnswerClaims,
        .. FixtureExpectations.ReleaseLedgerClaims,
        .. Reading.ReadSurface.ChartGraphRows,
        .. ComponentAccess.AnalystJobRows,
        .. SchemaColumns.AnalystStoreRows,
        .. FixtureExpectations.AnalystRows,
        .. Reading.ReadSurface.AnalystPageRows,
        .. Reading.ReadSurface.PaperPageRows,
    ];

    // The rows the document loses after phase 19's report, each counted by the report of the phase that landed it and in
    // no claim now: the 19.4 correction's table of every account, which the page's parts replace.
    internal static string[] TakenOutAfterPhaseNineteen => [.. Reading.ReadSurface.RetiredPaperRows];

    // How many more rows each part of phase 19 landed than its count in the plan, a negative figure where it landed fewer.
    // 19.0, none as counted. 19.1, one as counted. 19.2, nine of twelve: the readings store, three marks, Tonight's two rows,
    // the date line's night, the chart of what is behind a stock on its card and on its page and five rows moved to the Run
    // page, less the night header's four parts and the reason totals, the night's write of the readings landing as words in
    // section 14's row that stood. 19.3, six of eight: seventeen parts for eleven retired. 19.4, thirteen of fifteen: the two
    // stores, section 18's rule, paper trading's eight rows and the trade log's two, the monthly run's per-rule steps landing
    // as words in section 14's row that stood. 19.5, eight of seven: nine rows, the Run page's corrections and section 17's
    // two, less the sector strip's four. 19.6, three of two: its two and its correction's Write now. 19.7, five of fifteen:
    // twenty-four rows and parts less nineteen retired, the retired loop's regions taking thirteen more than the six counted.
    // 19.8, one as counted: Tonight's card of the stocks one gate short, the operator's addendum of 2026-10-15, its paper
    // trading figure and key a picture of claims placed elsewhere.
    static readonly (string Checkpoint, int More)[] PhaseNineteenMore =
    [
        ("19.0", 0),
        ("19.1", 0),
        ("19.2", -3),
        ("19.3", -2),
        ("19.4", -2),
        ("19.5", 1),
        ("19.6", 1),
        ("19.7", -10),
        ("19.8", 0),
    ];

    [Fact]
    public void ThePhaseNineteenPairIsCheckedAgainstTheActualWithEveryClaimThatMovedNamed()
    {
        // The pair and each part's count read off the plan, so the figures checked are the ones it carries.
        const string pair = @"([\d,]+) claims and \1 PASS after 18\.5\. After 19\.8 about ([\d,]+), within ([\d,]+) to ([\d,]+):\s*- none at 19\.0, [^;]*;\s*- about (\d+) at 19\.1, [^;]*;\s*- about (\d+) at 19\.2, [^;]*;\s*- about (\d+) at 19\.3, [^;]*;\s*- about (\d+) at 19\.4, [^;]*;\s*- about (\d+) at 19\.5, [^;]*;\s*- about (\d+) at 19\.6, [^;]*;\s*- about (\d+) at 19\.7, [^;]*;\s*- about (\d+) at 19\.8\.";
        var stated = Regex.Match(Corpus.Read("docs/BUILD_PLAN.md"), pair.Replace(" ", @"\s+", StringComparison.Ordinal));

        Assert.True(stated.Success, "The plan states no pair for phase 19.");

        int Figure(int group) => int.Parse(stated.Groups[group].Value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture);

        var (before, predicted, low, high) = (Figure(1), Figure(2), Figure(3), Figure(4));
        int[] counted = [0, Figure(5), Figure(6), Figure(7), Figure(8), Figure(9), Figure(10), Figure(11), Figure(12)];

        Assert.Equal(predicted, before + counted.Sum());

        // What each part landed: its count in the plan and the rows it landed beyond it.
        var landed = PhaseNineteenLanded;

        Assert.Equal(landed.Select(part => part.Checkpoint), PhaseNineteenMore.Select(part => part.Checkpoint));
        Assert.Equal(
            counted.Select((count, at) => count + PhaseNineteenMore[at].More),
            landed.Select(part => part.Added.Length - part.Taken.Length));

        // The rows the lists the rest of the harness reads name, the same rows: what phase 18's report names after it, less
        // what phase 19's own report names after it, and what it takes out, the claims 19.7 retired among them.
        Assert.Equal(AfterPhaseEighteen.Except(AfterPhaseNineteen, StringComparer.Ordinal).Order(StringComparer.Ordinal), landed.SelectMany(part => part.Added).Order(StringComparer.Ordinal));
        Assert.Equal(TakenOutAfterPhaseEighteen.Except(TakenOutAfterPhaseNineteen, StringComparer.Ordinal).Order(StringComparer.Ordinal), landed.SelectMany(part => part.Taken).Order(StringComparer.Ordinal));

        var rows = landed.SelectMany(part => part.Added).ToArray();
        var taken = landed.SelectMany(part => part.Taken).ToArray();
        var actual = before + rows.Length - taken.Length;

        // The actual stands inside the range the plan stated, short of the pair by the rows each part landed fewer than its
        // count, named above.
        Assert.Equal(predicted + PhaseNineteenMore.Sum(part => part.More), actual);
        Assert.InRange(actual, low, high);

        // Every row the phase added reached once and passing, every row it took out reached by no claim, and the report
        // holding the phase's rows over what stood before it, none out of scope and none unexamined.
        var report = Report();
        var total = actual + AfterPhaseNineteen.Length - TakenOutAfterPhaseNineteen.Length;

        // A row a later correction took out is counted where it landed and is in no claim now.
        Assert.Equal(rows.Length, rows.Distinct(StringComparer.Ordinal).Count());
        Assert.All(rows.Except(TakenOutAfterPhaseNineteen), key => Assert.Equal(Verdict.Pass, Assert.Single(report.Claims, claim => CheckReach.Key(claim.Table, claim.Subject) == key).Verdict));
        Assert.All(taken.Concat(rows.Intersect(TakenOutAfterPhaseNineteen)), key => Assert.DoesNotContain(report.Claims, claim => CheckReach.Key(claim.Table, claim.Subject) == key));
        Assert.Equal(
            (total, 0, 0, total),
            (report.Claims.Count, report.Count(Verdict.OutOfScope), report.Count(Verdict.Unexamined), report.Count(Verdict.Pass)));

        // Stated, so a claim added or lost without being named here moves this rather than the sum.
        Assert.Equal((1267, 1328, 1290, 1370, 1313), (before, predicted, low, high, actual));
    }
}
