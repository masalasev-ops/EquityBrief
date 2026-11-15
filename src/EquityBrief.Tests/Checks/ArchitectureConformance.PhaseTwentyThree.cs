using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Checks;

// architecture-conformance, 23.11: phase 23's pair read off the plan and checked against the actual, every row the phase
// added reached once and passing, and each checkpoint's count against the rows it landed with where every difference went.
public partial class ArchitectureConformance
{
    // The rows each checkpoint of phase 23 landed, in the plan's order, and the rows each took out, none: none at 23.0; 23.1's
    // data checker and history end filings, their store, section 17's four rows, section 18's row and the History page's two;
    // 23.2's pooled test, its two stores, section 17's row, section 18's row and the three page rows; 23.3's long history's
    // three components, its three stores, section 17's four rows, section 18's row and the History page's row; 23.4's long
    // price pull, its six stores, section 17's row, section 18's two rows and the History page's row, and its correction's
    // History page's marks owed; 23.5's long fact pull and long first-year reader, their store and section 17's row; 23.6's
    // long history reader and long ledger reader, their three stores, section 17's row, section 18's row and the History
    // page's row; 23.7's section 17 row of the long test and paper trading's row of the verdict over the long history; 23.8's
    // long search and plan point test, their stores, section 17's two rows and the Loop page's row; 23.9's sector splits
    // reader, section 17's two rows and the History page's labels row; at 23.10's second part the match check's catalogue and
    // matrix rows, section 14's step asking for the reported quarters taken out; and none at its correction or 23.11.
    static (string Checkpoint, string[] Added, string[] Taken)[] PhaseTwentyThreeLanded =>
    [
        ("23.0", [], []),
        ("23.1", [.. FixtureExpectations.DataCheckRows], []),
        ("23.2", [.. FixtureExpectations.PooledRows], []),
        ("23.3", [.. FixtureExpectations.LongMembershipRows], []),
        ("23.4", [.. FixtureExpectations.LongPricesRows], []),
        ("23.4 correction", [.. Reading.ReadSurface.MarkingPageRows], []),
        ("23.5", [.. FixtureExpectations.LongFactsRows], []),
        ("23.6", [.. FixtureExpectations.LongLedgerRows], []),
        ("23.7", [.. FixtureExpectations.LongTestRows], []),
        ("23.8", [.. FixtureExpectations.LongSearchRows], []),
        ("23.9", [.. FixtureExpectations.SectorSplitsRows], []),
        ("23.10", [.. MatchCheckRows], [CheckReach.Key(NightlyRunSteps.Heading, RetiredQuartersStep)]),
        ("23.10 correction", [], []),
        ("23.11", [], []),
    ];

    // The rows corrections of phase 22's work landed while phase 23 was built, named beside the pair and counted in none of
    // it: the 22.6 correction of 2026-11-09's setup's record over the long history on a pick's card, on Home's idea and on a
    // tile; the 22.16 corrections' Home picks watched and not drawn and a plan that buys at a zone; and the 22.17
    // correction's records and picks watched on the read-only copy.
    internal static string[] BesidePhaseTwentyThree =>
    [
        .. Reading.ReadSurface.SetupRecordRows,
        .. Reading.ReadSurface.HomeWatchingRows,
        .. Reading.ReadSurface.ZonePlanRows,
        .. Reading.ReadSurface.CopyRecordRows,
    ];

    // How many more rows each checkpoint landed than its count in the plan, a negative figure where it landed fewer, each
    // checkpoint's rows named in its entry and in the lists above. 23.0, none as counted. 23.1, twelve of seven; 23.2, nine of
    // six; 23.3, fifteen of nine; 23.4, twelve of eleven, and its correction one, which no count held; 23.5, eight as counted;
    // 23.6, eleven of nine; 23.8, nine of three; 23.9, five of three: each component's catalogue and matrix rows, each store
    // and each of section 17's settings landing as a row of its own where the plan counted them together. 23.7, two of four,
    // its monthly step and its store landing as words in rows that stood. 23.10, one of seven: the swing filter's and the
    // book's hooks, the profit check and the conditions were tabled for phase 24 and from its second part landed as words in
    // rows that stood, as the plans, the marks and the cards' zones did, its second part adding the match check's catalogue
    // and matrix rows and retiring section 14's step asking for the reported quarters, whose fetch asks inside the swing
    // filter's step; its correction none. 23.11, none of one.
    static readonly (string Checkpoint, int More)[] PhaseTwentyThreeMore =
    [
        ("23.0", 0),
        ("23.1", 5),
        ("23.2", 3),
        ("23.3", 6),
        ("23.4", 1),
        ("23.4 correction", 1),
        ("23.5", 0),
        ("23.6", 2),
        ("23.7", -2),
        ("23.8", 6),
        ("23.9", 2),
        ("23.10", -6),
        ("23.10 correction", 0),
        ("23.11", -1),
    ];

    [Fact]
    public void ThePhaseTwentyThreePairIsCheckedAgainstTheActualWithEveryClaimThatMovedNamed()
    {
        // The pair and each checkpoint's count read off the plan, so the figures checked are the ones it carries.
        const string pair = @"([\d,]+) claims and \1 PASS on main at [0-9a-f]{8}\. After 23\.11 about ([\d,]+), within ([\d,]+) to ([\d,]+):\s*- none at 23\.0, [^;]*;\s*- about (\d+) at 23\.1, [^;]*;\s*- about (\d+) at 23\.2, [^;]*;\s*- about (\d+) at 23\.3, [^;]*;\s*- about (\d+) at 23\.4, [^;]*;\s*- about (\d+) at 23\.5, [^;]*;\s*- about (\d+) at 23\.6, [^;]*;\s*- about (\d+) at 23\.7, [^;]*;\s*- about (\d+) at 23\.8, [^;]*;\s*- about (\d+) at 23\.9, [^;]*;\s*- about (\d+) at 23\.10, [^;]*;\s*- about (\d+) at 23\.11\.";
        var stated = Regex.Match(Corpus.Read("docs/BUILD_PLAN.md"), pair.Replace(" ", @"\s+", StringComparison.Ordinal));

        Assert.True(stated.Success, "The plan states no pair for phase 23.");

        int Figure(int group) => int.Parse(stated.Groups[group].Value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture);

        var (before, predicted, low, high) = (Figure(1), Figure(2), Figure(3), Figure(4));
        var byCheckpoint = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["23.1"] = Figure(5), ["23.2"] = Figure(6), ["23.3"] = Figure(7), ["23.4"] = Figure(8), ["23.5"] = Figure(9),
            ["23.6"] = Figure(10), ["23.7"] = Figure(11), ["23.8"] = Figure(12), ["23.9"] = Figure(13), ["23.10"] = Figure(14),
            ["23.11"] = Figure(15),
        };
        var landed = PhaseTwentyThreeLanded;
        int[] counted = [.. landed.Select(part => byCheckpoint.GetValueOrDefault(part.Checkpoint))];

        Assert.Equal(landed.Select(part => part.Checkpoint), PhaseTwentyThreeMore.Select(part => part.Checkpoint));

        // What each checkpoint landed: its count in the plan and the rows it landed beyond it.
        int Net((string Checkpoint, string[] Added, string[] Taken) part) => part.Added.Length - part.Taken.Length;

        Assert.Equal(counted.Select((count, at) => count + PhaseTwentyThreeMore[at].More), landed.Select(Net));

        // The pair the plan states is the figure it starts from and the counts it states.
        Assert.Equal(predicted, before + counted.Sum());

        // The actual stands inside the range the plan stated, past the pair by the net of what each checkpoint landed more or
        // fewer than its count, named above.
        var actual = before + landed.Sum(Net);

        Assert.Equal(predicted + PhaseTwentyThreeMore.Sum(part => part.More), actual);
        Assert.InRange(actual, low, high);

        // Every row the phase added and every row beside it is one phase 22's pair names after its report, the two apart.
        var rows = landed.SelectMany(part => part.Added).ToArray();

        Assert.Empty(rows.Intersect(BesidePhaseTwentyThree, StringComparer.Ordinal));
        Assert.Subset(AfterPhaseTwentyTwo.ToHashSet(StringComparer.Ordinal), rows.Concat(BesidePhaseTwentyThree).ToHashSet(StringComparer.Ordinal));

        // Every row the phase added reached once and passing, and the report holding the phase's rows and the rows beside
        // them over what stood before it, none out of scope and none unexamined.
        var report = Report();
        var total = actual + BesidePhaseTwentyThree.Length;

        Assert.Equal(rows.Length, rows.Distinct(StringComparer.Ordinal).Count());
        Assert.All(rows, key => Assert.Equal(Verdict.Pass, Assert.Single(report.Claims, claim => CheckReach.Key(claim.Table, claim.Subject) == key).Verdict));
        Assert.Equal(
            (total, 0, 0, total),
            (report.Claims.Count, report.Count(Verdict.OutOfScope), report.Count(Verdict.Unexamined), report.Count(Verdict.Pass)));

        // Stated, so a claim added or lost without being named here moves this rather than the sum.
        Assert.Equal((1534, 1602, 1580, 1625, 1619, 6), (before, predicted, low, high, actual, BesidePhaseTwentyThree.Length));
    }
}
