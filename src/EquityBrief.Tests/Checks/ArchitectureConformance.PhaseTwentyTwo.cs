using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Checks;

// architecture-conformance, 22.12: phase 22's pair read off the plan and checked against the actual, every row the phase
// added reached once and passing, and each part's count against the rows it landed with where every difference went.
public partial class ArchitectureConformance
{
    // The rows each part of phase 22 landed, in the plan's order, and the rows it took out, none: none at 22.0; 22.1's
    // dip family, its catalogue and matrix rows, its four stores, section 17's row and section 18's four, its card on
    // Tonight and its trades on Past picks; 22.2's grid, its row for a market series missing a session and the Ledger
    // page's dip counts; none at 22.3; 22.4's brief read; none for the correction on the brief read's track; none at
    // 22.5; 22.6's minute and the words at Tonight's foot; none at 22.7; 22.8's scorecard, its catalogue and matrix rows,
    // its store, section 17's numbers, section 18's three and its card on the chart page and the full page; 22.9's quality
    // family, its catalogue and matrix rows, its three stores, section 17's settings, section 18's two, its card on
    // Tonight and its step in section 14; 22.10's grid; and none at 22.11 and 22.12.
    static (string Checkpoint, string[] Added, string[] Taken)[] PhaseTwentyTwoLanded =>
    [
        ("22.0", [], []),
        ("22.1", [.. ComponentAccess.DipRows, .. SchemaColumns.DipStoreRows, .. FixtureExpectations.DipClaims, .. Reading.ReadSurface.DipPageRows], []),
        ("22.2", [.. FixtureExpectations.DipLedgerClaims, .. Reading.ReadSurface.DipLedgerPageRows], []),
        ("22.3", [], []),
        ("22.4", [.. Reading.ReadSurface.BriefRows], []),
        ("22.4 correction", [], []),
        ("22.5", [], []),
        ("22.6", [.. Reading.ReadSurface.TonightRows], []),
        ("22.7", [], []),
        ("22.8", [.. ComponentAccess.ScorerRows, .. SchemaColumns.ScoreStoreRows, .. FixtureExpectations.ScorecardClaims, .. Reading.ReadSurface.FundamentalsRows], []),
        ("22.9", [.. ComponentAccess.QualityRows, .. SchemaColumns.QualityStoreRows, .. FixtureExpectations.QualityClaims, .. Reading.ReadSurface.QualityPageRows, CheckReach.Key(NightlyRunSteps.Heading, NightlyRun.QualityStep)], []),
        ("22.10", [.. FixtureExpectations.QualityHistoryClaims], []),
        ("22.11", [], []),
        ("22.12", [], []),
    ];

    // The rows the document gains after phase 22's report, named beside the pair and never counted in it: 22.13's ten, the
    // Fundamentals family's catalogue and matrix rows, its three stores, its step in section 14, its card on Tonight,
    // section 17's starting settings and section 18's two.
    internal static string[] AfterPhaseTwentyTwo =>
    [
        .. ComponentAccess.ImprovingRows,
        .. SchemaColumns.ImprovingStoreRows,
        .. FixtureExpectations.ImprovingClaims,
        .. Reading.ReadSurface.ImprovingPageRows,
        CheckReach.Key(NightlyRunSteps.Heading, NightlyRun.ImprovingStep),
    ];

    // The rows the document loses after phase 22's report: none.
    internal static string[] TakenOutAfterPhaseTwentyTwo => [];

    // The part whose measured figure the pair after 22.12 starts from: the plan restated the pair at 22.7 from the claims
    // measured after 22.6, so the parts after it are counted from that figure and the parts up to it are each read
    // against their count and the rows they landed.
    const string PairMeasuredAfter = "22.6";

    // How many more rows each part landed than its count in the plan, a negative figure where it landed fewer. 22.0, none
    // as counted. 22.1, thirteen as counted. 22.2, three as counted. 22.3, none as counted. 22.4, one of two: its row of
    // chips landing as words in the brief read's row. The correction on its track, none as counted. 22.5, none of forty:
    // section 15.23's four tables of plain words are placed whole and held word for word by the plain-words check, a claim
    // no row. 22.6, two of six: Tonight in one minute and the words at its foot, its table of tomorrow's buys, its tiles,
    // the versions' dropdown and its folds landing as clauses in the rows section 15.7 extends. 22.7, none as counted.
    // 22.8, nine of eighteen: the parts its count named came to seventeen, ten of them the readings it adds to the
    // ledger's catalogue, which are no rows of a table the harness reads, and its catalogue and matrix rows, which the count
    // left out, landed. 22.9, ten as counted. 22.10, one as counted. 22.11 and 22.12, none as counted.
    static readonly (string Checkpoint, int More)[] PhaseTwentyTwoMore =
    [
        ("22.0", 0),
        ("22.1", 0),
        ("22.2", 0),
        ("22.3", 0),
        ("22.4", -1),
        ("22.4 correction", 0),
        ("22.5", -40),
        ("22.6", -4),
        ("22.7", 0),
        ("22.8", -9),
        ("22.9", 0),
        ("22.10", 0),
        ("22.11", 0),
        ("22.12", 0),
    ];

    [Fact]
    public void ThePhaseTwentyTwoPairIsCheckedAgainstTheActualWithEveryClaimThatMovedNamed()
    {
        // The pair and each part's count read off the plan, so the figures checked are the ones it carries.
        const string pair = @"([\d,]+) claims and \1 PASS on main at [0-9a-f]{8}, and ([\d,]+) on main at [0-9a-f]{8} after 22\.6\. After 22\.12 about ([\d,]+), within ([\d,]+) to ([\d,]+):\s*- none at 22\.0, [^;]*;\s*- about (\d+) at 22\.1, [^;]*;\s*- about (\d+) at 22\.2, [^;]*;\s*- none at 22\.3, [^;]*;\s*- about (\d+) at 22\.4, [^;]*;\s*- none for the correction on the brief read's track, [^;]*;\s*- about (\d+) at 22\.5, [^;]*;\s*- about (\d+) at 22\.6, [^;]*;\s*- none at 22\.7, [^;]*;\s*- about (\d+) at 22\.8, [^;]*;\s*- about (\d+) at 22\.9, [^;]*;\s*- about (\d+) at 22\.10, [^;]*;\s*- none at 22\.11, [^;]*;\s*- none at 22\.12\.";
        var stated = Regex.Match(Corpus.Read("docs/BUILD_PLAN.md"), pair.Replace(" ", @"\s+", StringComparison.Ordinal));

        Assert.True(stated.Success, "The plan states no pair for phase 22.");

        int Figure(int group) => int.Parse(stated.Groups[group].Value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture);

        var (before, afterSix, predicted, low, high) = (Figure(1), Figure(2), Figure(3), Figure(4), Figure(5));
        int[] counted = [0, Figure(6), Figure(7), 0, Figure(8), 0, Figure(9), Figure(10), 0, Figure(11), Figure(12), Figure(13), 0, 0];

        var landed = PhaseTwentyTwoLanded;

        Assert.Equal(landed.Select(part => part.Checkpoint), PhaseTwentyTwoMore.Select(part => part.Checkpoint));
        Assert.Equal(counted.Length, landed.Length);

        // What each part landed: its count in the plan and the rows it landed beyond it.
        Assert.Equal(
            counted.Select((count, at) => count + PhaseTwentyTwoMore[at].More),
            landed.Select(part => part.Added.Length - part.Taken.Length));

        // The figure the pair starts from: phase 20's actual less 20.11 to 20.13's rows, which landed after it was measured.
        var lateTwenty = PhaseTwentyLanded[^1];

        Assert.Equal("20.11 to 20.13", lateTwenty.Checkpoint);
        Assert.Equal(1405 - lateTwenty.Added.Length, before);

        // The figure measured after 22.6: the figure before the phase, 20.11 to 20.13's rows and what the parts up to 22.6
        // landed.
        var six = Array.FindIndex(landed, part => part.Checkpoint == PairMeasuredAfter);
        int Net((string Checkpoint, string[] Added, string[] Taken) part) => part.Added.Length - part.Taken.Length;

        Assert.Equal(afterSix, before + lateTwenty.Added.Length + landed.Take(six + 1).Sum(Net));

        // The pair after 22.12: the figure measured after 22.6 and the counts of the parts after it.
        Assert.Equal(predicted, afterSix + counted.Skip(six + 1).Sum());

        // The rows the lists the rest of the harness reads name, the same rows: what phase 20's report names after it, less
        // what phase 22's own report names after it, and what it takes out.
        Assert.Equal(
            AfterPhaseTwenty.Except(AfterPhaseTwentyTwo, StringComparer.Ordinal).Order(StringComparer.Ordinal),
            landed.SelectMany(part => part.Added).Order(StringComparer.Ordinal));
        Assert.Equal(
            TakenOutAfterPhaseTwenty.Except(TakenOutAfterPhaseTwentyTwo, StringComparer.Ordinal).Order(StringComparer.Ordinal),
            landed.SelectMany(part => part.Taken).Order(StringComparer.Ordinal));

        var rows = landed.SelectMany(part => part.Added).ToArray();
        var taken = landed.SelectMany(part => part.Taken).ToArray();
        var actual = afterSix + landed.Skip(six + 1).Sum(Net);

        // The actual stands inside the range the plan stated, short of the pair by the rows each part after 22.6 landed
        // fewer than its count, named above.
        Assert.Equal(predicted + PhaseTwentyTwoMore.Skip(six + 1).Sum(part => part.More), actual);
        Assert.InRange(actual, low, high);

        // Every row the phase added reached once and passing, and the report holding the phase's rows over what stood
        // before it, none out of scope and none unexamined.
        var report = Report();
        var total = actual + AfterPhaseTwentyTwo.Length - TakenOutAfterPhaseTwentyTwo.Length;

        Assert.Equal(rows.Length, rows.Distinct(StringComparer.Ordinal).Count());
        Assert.All(rows, key => Assert.Equal(Verdict.Pass, Assert.Single(report.Claims, claim => CheckReach.Key(claim.Table, claim.Subject) == key).Verdict));
        Assert.All(taken, key => Assert.DoesNotContain(report.Claims, claim => CheckReach.Key(claim.Table, claim.Subject) == key));
        Assert.Equal(
            (total, 0, 0, total),
            (report.Claims.Count, report.Count(Verdict.OutOfScope), report.Count(Verdict.Unexamined), report.Count(Verdict.Pass)));

        // Stated, so a claim added or lost without being named here moves this rather than the sum.
        Assert.Equal((1383, 1424, 1453, 1438, 1468, 1444), (before, afterSix, predicted, low, high, actual));
    }
}
