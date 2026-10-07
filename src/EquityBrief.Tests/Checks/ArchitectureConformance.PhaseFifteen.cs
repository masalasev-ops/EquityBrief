using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Checks;

// architecture-conformance, 15.6: phase 15's pair read off the plan and checked against the actual, every row the phase
// added reached once and passing, and every claim its counts held that landed as no row named with where it went.
public partial class ArchitectureConformance
{
    // The rows of phase 15 each checkpoint landed, in the plan's order, read on each use since the lists that hold them
    // stand in other files of this class: 15.1's fund's file row and the Run page's members, with its second half's
    // index families' catalogue and matrix rows; 15.2's readings with its second half's member readings; 15.3's holdings
    // rows; 15.4's sweep answers' catalogue and matrix rows; and 15.5's freezes' rows.
    static (string Checkpoint, string[] Rows)[] PhaseFifteenLanded =>
    [
        ("15.1", [.. NightlyRun.ThreeIndicesRows, .. IndexFamiliesRows]),
        ("15.2", [.. FixtureExpectations.ReadingsClaims, .. MemberReadingRows]),
        ("15.3", FixtureExpectations.HoldingsClaims),
        ("15.4", SweepAnswersRows),
        ("15.5", IndexRuleRows),
    ];

    // How many of each checkpoint's count landed as no row, and where they went. At 15.1, of about thirty: the membership
    // of three indices, the funds' files, each index's strength, breadth and market check, the six reports in turn and the
    // night's limits landed as words in rows that stood, the membership loader's and the request drain's catalogue rows,
    // section 14's steps and section 17's per-name, reports and deadline rows; the choice of index on five screens as
    // section 15's paragraphs; and the provisional rules' floors and gate at 15.2, as section 17's rows. At 15.2, of about
    // eighteen: the readings landed in four of section 17's rows and not a row a reading. At 15.3, of about eight: the pulls'
    // rows as words in the history pull's catalogue row and section 16's pulled history row, and membership as it stood as
    // section 13.9's paragraph. At 15.4, of about twelve: each index's sweep rows as section 13.9's paragraphs and words in
    // the sweep history's rows, the no-pass card's line as section 15.7's paragraph, and its failure row as the family
    // sweep's row 15.0 reworded. At 15.5, of about eight: the records' parts as section 15.10's paragraph and words in the
    // setup families' row that stood.
    static readonly (string Checkpoint, int Fewer)[] PhaseFifteenShort =
    [
        ("15.1", 26),
        ("15.2", 4),
        ("15.3", 6),
        ("15.4", 10),
        ("15.5", 3),
    ];

    // The rows the document gains after phase 15's report, named beside the pair and never counted in it: from 16.1 the
    // decision card's rows, the 16.1 ruling's section 18 rows on the local model's load and its settings, from 16.2
    // the taken trades' rows, from 16.3 the follower's, and every row after phase 16's report.
    internal static string[] AfterPhaseFifteen => [.. CardRows, .. FixtureExpectations.LocalModelClaims, .. TakenRows, .. FollowerRows, .. AfterPhaseSixteen];

    // 16.3's rows: the taken trades' follower's catalogue and matrix rows, the operator's record's and the dividend
    // readings' stores, and section 17's three values and section 18's three rows.
    internal static string[] FollowerRows =>
    [
        CheckReach.Key(Scope.CatalogueTable, "Taken follower"),
        CheckReach.Key(Scope.MatrixTable, "Taken follower"),
        CheckReach.Key(Scope.StoresTable, "Taken records"),
        CheckReach.Key(Scope.StoresTable, "Dividend readings"),
        .. FixtureExpectations.FollowerClaims,
    ];

    // 16.2's rows: the taken trades' store, section 17's two values and section 18's two rows.
    internal static string[] TakenRows =>
    [
        CheckReach.Key(Scope.StoresTable, "Taken trades"),
        .. Reading.ReadSurface.TakenClaims,
    ];

    // 16.1's rows: the decision cards' and the rule recorder's catalogue and matrix rows, their two stores, section 17's
    // four values and section 18's four failures.
    internal static string[] CardRows =>
    [
        CheckReach.Key(Scope.CatalogueTable, "Decision cards"),
        CheckReach.Key(Scope.MatrixTable, "Decision cards"),
        CheckReach.Key(Scope.CatalogueTable, "Rule recorder"),
        CheckReach.Key(Scope.MatrixTable, "Rule recorder"),
        CheckReach.Key(Scope.StoresTable, "Decision cards"),
        CheckReach.Key(Scope.StoresTable, "Rule records"),
        .. FixtureExpectations.CardClaims,
    ];

    [Fact]
    public void ThePhaseFifteenPairIsCheckedAgainstTheActualWithEveryClaimThatMovedNamed()
    {
        // The pair and each checkpoint's count read off the plan, so the figures checked are the ones it carries.
        const string pair = @"(\d+) claims and \1 PASS before it\. After 15\.5 the pair is ([\d,]+) and \2, within ([\d,]+) to ([\d,]+): none at 15\.0, [^;]*; about (\d+) at 15\.1, [^;]*; about (\d+) at 15\.2, [^;]*; about (\d+) at 15\.3, [^;]*; about (\d+) at 15\.4, [^;]*; and about (\d+) at 15\.5,";
        var stated = Regex.Match(Corpus.Read("docs/BUILD_PLAN.md"), pair.Replace(" ", @"\s+", StringComparison.Ordinal));

        Assert.True(stated.Success, "The plan states no pair for phase 15.");

        int Figure(int group) => int.Parse(stated.Groups[group].Value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture);

        var (before, predicted, low, high) = (Figure(1), Figure(2), Figure(3), Figure(4));
        int[] counted = [Figure(5), Figure(6), Figure(7), Figure(8), Figure(9)];

        Assert.Equal(predicted, before + counted.Sum());

        // What 15.1 to 15.5 landed: each checkpoint's count in the plan less the claims it held that landed as no row.
        var landed = PhaseFifteenLanded;

        Assert.Equal(landed.Select(checkpoint => checkpoint.Checkpoint), PhaseFifteenShort.Select(checkpoint => checkpoint.Checkpoint));
        Assert.Equal(
            counted.Select((count, at) => count - PhaseFifteenShort[at].Fewer),
            landed.Select(checkpoint => checkpoint.Rows.Length));

        var rows = landed.SelectMany(checkpoint => checkpoint.Rows).ToArray();
        var actual = before + rows.Length;

        // The actual falls under the range the plan stated, by exactly the claims named as landing as no row.
        Assert.InRange(actual, before, low - 1);
        Assert.Equal(predicted - actual, PhaseFifteenShort.Sum(checkpoint => checkpoint.Fewer));

        // Every row the phase added reached once and passing, and the report holding the phase's rows over what stood
        // before it and the rows after its report, none out of scope and none unexamined.
        var report = Report();
        var total = actual + AfterPhaseFifteen.Length;

        Assert.Equal(rows.Length, rows.Distinct(StringComparer.Ordinal).Count());
        Assert.All(rows, key => Assert.Equal(Verdict.Pass, Assert.Single(report.Claims, claim => CheckReach.Key(claim.Table, claim.Subject) == key).Verdict));
        Assert.Equal(
            (total, 0, 0, total),
            (report.Claims.Count, report.Count(Verdict.OutOfScope), report.Count(Verdict.Unexamined), report.Count(Verdict.Pass)));

        // Stated, so a claim added or lost without being named here moves this rather than the sum.
        Assert.Equal((973, 1049, 1020, 1080, 27, 1000), (before, predicted, low, high, rows.Length, actual));
    }
}
