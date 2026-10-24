using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Checks;

// architecture-conformance, 20.10: phase 20's pair read off the plan and checked against the actual, every row the phase
// added reached once and passing, the row it took out reached by no claim, and each part's count against the rows it
// landed with where every difference went.
public partial class ArchitectureConformance
{
    // The rows the document gained after phase 19's report and before phase 20's plan stated its pair, which the figure the
    // pair starts from already counts: 19.10's three, 19.11's eleven, 19.12's three, 19.13's seven and the 17.3 correction
    // of 2026-10-16's three.
    static string[] BeforePhaseTwenty =>
    [
        .. FixtureExpectations.TradeFillRows,
        .. Reading.ReadSurface.ChartRows,
        .. Reading.ReadSurface.ReadingLinkRows,
        CheckReach.Key(Scope.StoresTable, "Reading links"),
        .. FixtureExpectations.ReadingLinkClaims,
        .. ComponentAccess.InsiderTradeRows,
        .. SchemaColumns.InsiderStoreRows,
        .. FixtureExpectations.InsiderClaims,
        .. Reading.ReadSurface.InsiderRows,
        .. ComponentAccess.SecTickerRows,
        .. SchemaColumns.SecTickerStoreRows,
    ];

    // The 20.3 correction of 2026-10-22's store, listed with 20.2's stores and counted with the checkpoint it corrects.
    static string ReleaseChecksRow => CheckReach.Key(Scope.StoresTable, "Release checks");

    // The rows each part of phase 20 landed, in the plan's order, and the rows it took out, each correction counted with
    // the checkpoint it corrects: the 19.4 correction of 2026-10-22, landed while the phase was built, paper trading's
    // seven parts less its table of every account; none at 20.0; 20.1's local run, its pins' store, section 17's and
    // section 18's rows and the Run page's region; 20.2's release fetch and its two stores; 20.3's accuracy report, its
    // three stores and its correction's release checks; 20.4's card row and window; 20.5 to 20.7's comparison; 20.8's
    // usefulness store and its pages' rows; 20.9's generator; none at 20.10; and 20.11 to 20.13 on the operator's rulings
    // after the plan stated its pair, 20.11's freezes none, 20.12's graphs and 20.13's analysts.
    static (string Checkpoint, string[] Added, string[] Taken)[] PhaseTwentyLanded =>
    [
        ("corrections", [.. Reading.ReadSurface.PaperPageRows], [.. Reading.ReadSurface.RetiredPaperRows]),
        ("20.0", [], []),
        ("20.1", [.. ComponentAccess.LocalRunRows, .. SchemaColumns.ReadingPinStoreRows, .. FixtureExpectations.LocalRunClaims, .. Reading.ReadSurface.LocalModelRows], []),
        ("20.2", [.. ComponentAccess.ReleaseFetchRows, .. SchemaColumns.ReleaseStoreRows.Where(row => row != ReleaseChecksRow), .. FixtureExpectations.ReleaseFetchClaims], []),
        ("20.3", [.. ComponentAccess.ReleaseAccuracyRows, .. SchemaColumns.ReleaseReadingStoreRows, .. FixtureExpectations.ReleaseReadingClaims, ReleaseChecksRow], []),
        ("20.4", [.. FixtureExpectations.ToneVariantClaims], []),
        ("20.5 to 20.7", [.. ComponentAccess.LocalComparisonRows], []),
        ("20.8", [.. SchemaColumns.ReleaseAnswerStoreRows, .. Reading.ReadSurface.ReleaseAnswerClaims, .. FixtureExpectations.ReleaseLedgerClaims], []),
        ("20.9", [.. ComponentAccess.IdeaGeneratorRows, .. SchemaColumns.LoopIdeaStoreRows, .. Reading.ReadSurface.LoopIdeaClaims, .. FixtureExpectations.IdeaGeneratorClaims], []),
        ("20.10", [], []),
        ("20.11 to 20.13", [.. Reading.ReadSurface.ChartGraphRows, .. ComponentAccess.AnalystJobRows, .. SchemaColumns.AnalystStoreRows, .. FixtureExpectations.AnalystRows, .. Reading.ReadSurface.AnalystPageRows], []),
    ];

    // The rows the document gains after phase 20's report, named beside the pair and never counted in it, which phase 22's
    // own report counts: 22.1's thirteen, the dip family's catalogue and matrix rows, its four stores, section 17's row,
    // section 18's four, its card on Tonight and its trades on Past picks, 22.2's three, section 17's grid of the dip's
    // sweep, section 18's row for a market series it reads missing a session and the Ledger page's dip counts, 22.4's
    // one, the chart page's brief read, and 22.6's two, Tonight in one minute and the page's words at its foot.
    internal static string[] AfterPhaseTwenty =>
    [
        .. ComponentAccess.DipRows,
        .. SchemaColumns.DipStoreRows,
        .. FixtureExpectations.DipClaims,
        .. Reading.ReadSurface.DipPageRows,
        .. FixtureExpectations.DipLedgerClaims,
        .. Reading.ReadSurface.DipLedgerPageRows,
        .. Reading.ReadSurface.BriefRows,
        .. Reading.ReadSurface.TonightRows,
    ];

    // The rows the document loses after phase 20's report: none.
    internal static string[] TakenOutAfterPhaseTwenty => [];

    // How many more rows each part landed than its count in the plan, a negative figure where it landed fewer. The
    // corrections, counted none, six: paper trading's seven parts for its table of every account. 20.0, none as counted.
    // 20.1, nine as counted. 20.2, six of five: the release fetch's catalogue and matrix rows, its two stores, section 17's
    // row and section 18's. 20.3, eight as counted: the accuracy report's catalogue and matrix rows, the three stores of the
    // releases' filers, readings and probes, section 17's row and section 18's, and its correction's release checks. 20.4,
    // two as counted. 20.5 to 20.7, two as counted. 20.8, four of six: the usefulness store, the Ledger page's and the Loop
    // page's rows and section 17's window row, the readings' count and the monthly run's step landing as words in rows that
    // stood. 20.9, six as counted. 20.10, none of one: the report adds no row. 20.11 to 20.13, counted none since the plan
    // stated its pair before the operator's rulings added them, twenty-two: 20.11's freezes none, 20.12's graphs and their
    // three marks, and 20.13's eighteen.
    static readonly (string Checkpoint, int More)[] PhaseTwentyMore =
    [
        ("corrections", 6),
        ("20.0", 0),
        ("20.1", 0),
        ("20.2", 1),
        ("20.3", 0),
        ("20.4", 0),
        ("20.5 to 20.7", 0),
        ("20.8", -2),
        ("20.9", 0),
        ("20.10", -1),
        ("20.11 to 20.13", 22),
    ];

    [Fact]
    public void ThePhaseTwentyPairIsCheckedAgainstTheActualWithEveryClaimThatMovedNamed()
    {
        // The pair and each part's count read off the plan, so the figures checked are the ones it carries.
        const string pair = @"([\d,]+) claims and \1 PASS after phase 17's sign-off\. After 20\.10 about ([\d,]+), within ([\d,]+) to ([\d,]+):\s*- none at 20\.0, [^;]*;\s*- about (\d+) at 20\.1, [^;]*;\s*- about (\d+) at 20\.2, [^;]*;\s*- about (\d+) at 20\.3, [^;]*;\s*- about (\d+) at 20\.4, [^;]*;\s*- about (\d+) at 20\.5 to 20\.7, [^;]*;\s*- about (\d+) at 20\.8, [^;]*;\s*- about (\d+) at 20\.9, [^;]*;\s*- about (\d+) at 20\.10\.";
        var stated = Regex.Match(Corpus.Read("docs/BUILD_PLAN.md"), pair.Replace(" ", @"\s+", StringComparison.Ordinal));

        Assert.True(stated.Success, "The plan states no pair for phase 20.");

        int Figure(int group) => int.Parse(stated.Groups[group].Value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture);

        var (before, predicted, low, high) = (Figure(1), Figure(2), Figure(3), Figure(4));
        int[] counted = [0, 0, Figure(5), Figure(6), Figure(7), Figure(8), Figure(9), Figure(10), Figure(11), Figure(12), 0];

        Assert.Equal(predicted, before + counted.Sum());

        // The figure the pair starts from: phase 19's actual and the rows landed between its report and phase 20's plan.
        Assert.Equal(1313 + BeforePhaseTwenty.Length, before);

        // What each part landed: its count in the plan and the rows it landed beyond it.
        var landed = PhaseTwentyLanded;

        Assert.Equal(landed.Select(part => part.Checkpoint), PhaseTwentyMore.Select(part => part.Checkpoint));
        Assert.Equal(
            counted.Select((count, at) => count + PhaseTwentyMore[at].More),
            landed.Select(part => part.Added.Length - part.Taken.Length));

        // The rows the lists the rest of the harness reads name, the same rows: what phase 19's report names after it, less
        // what stood before phase 20's plan and what phase 20's own report names after it, and what it takes out.
        Assert.Equal(
            AfterPhaseNineteen.Except(BeforePhaseTwenty, StringComparer.Ordinal).Except(AfterPhaseTwenty, StringComparer.Ordinal).Order(StringComparer.Ordinal),
            landed.SelectMany(part => part.Added).Order(StringComparer.Ordinal));
        Assert.Equal(
            TakenOutAfterPhaseNineteen.Except(TakenOutAfterPhaseTwenty, StringComparer.Ordinal).Order(StringComparer.Ordinal),
            landed.SelectMany(part => part.Taken).Order(StringComparer.Ordinal));

        var rows = landed.SelectMany(part => part.Added).ToArray();
        var taken = landed.SelectMany(part => part.Taken).ToArray();
        var actual = before + rows.Length - taken.Length;

        // The actual stands inside the range the plan stated, past the pair by the rows each part landed beyond its count,
        // named above, 20.11 to 20.13's among them.
        Assert.Equal(predicted + PhaseTwentyMore.Sum(part => part.More), actual);
        Assert.InRange(actual, low, high);

        // Every row the phase added reached once and passing, the row it took out reached by no claim, and the report
        // holding the phase's rows over what stood before it and the rows after its report, none out of scope and none
        // unexamined.
        var report = Report();
        var total = actual + AfterPhaseTwenty.Length - TakenOutAfterPhaseTwenty.Length;

        Assert.Equal(rows.Length, rows.Distinct(StringComparer.Ordinal).Count());
        Assert.All(rows, key => Assert.Equal(Verdict.Pass, Assert.Single(report.Claims, claim => CheckReach.Key(claim.Table, claim.Subject) == key).Verdict));
        Assert.All(taken, key => Assert.DoesNotContain(report.Claims, claim => CheckReach.Key(claim.Table, claim.Subject) == key));
        Assert.Equal(
            (total, 0, 0, total),
            (report.Claims.Count, report.Count(Verdict.OutOfScope), report.Count(Verdict.Unexamined), report.Count(Verdict.Pass)));

        // Stated, so a claim added or lost without being named here moves this rather than the sum.
        Assert.Equal((1340, 1379, 1345, 1415, 1405), (before, predicted, low, high, actual));
    }
}
