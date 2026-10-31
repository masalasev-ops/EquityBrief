using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Checks;

// architecture-conformance, 21.15: phase 21's pair read off the plan and checked against the actual, every row the phase
// added reached once and passing, and each checkpoint's count against the rows it landed with where every difference
// went. Phase 21 was built after phase 22's report, so the rows phase 22's pair named after it are phase 21's beside
// 22.13's and 22.14's.
public partial class ArchitectureConformance
{
    // The rows each checkpoint of phase 21 landed, in the order they were built, and the rows each took out, none: none at
    // 21.0, 21.1 and its correction or 21.2; 21.3's score audit's catalogue and matrix rows; 21.4's exposure's, ranking's
    // and chosen readings' rows in section 17, and its correction's section 18 row for a history build asked for a
    // session the night wrote; none at 21.5; 21.6's trends fetcher's, ratings refresh's and short interest reader's
    // catalogue and matrix rows, their two stores, section 17's three rows, section 18's two, the Run page's count of the
    // refresh's runs and the Ledger page's line on the readings' sources, and none for its correction; 21.7's setups'
    // chance on a pick's card; 21.8's ranking variant in section 17; 21.9's graded exposure variant and paper trading's
    // trade sized at half; 21.10's windows and floors of choosing a version and the Loop page's row of it; none at 21.16;
    // 21.11's combined account, its settings and section 18's row for a stock two families picked; 21.12's housekeeping
    // asks; 21.13's market strip on Tonight and on the copy; 21.14's drift surprises catalogue and matrix rows and paper
    // trading's tone variant's line; and none at 21.15.
    static (string Checkpoint, string[] Added, string[] Taken)[] PhaseTwentyOneLanded =>
    [
        ("21.0", [], []),
        ("21.1", [], []),
        ("21.1 correction", [], []),
        ("21.2", [], []),
        ("21.3", [CheckReach.Key(Scope.CatalogueTable, "Score audit"), CheckReach.Key(Scope.MatrixTable, "Score audit")], []),
        ("21.4", [.. FixtureExpectations.HookFormClaims, .. FixtureExpectations.ChosenReadingClaims], []),
        ("21.4 correction", [CheckReach.Key(Scope.FailureTable, "A history build asked for a session the night wrote")], []),
        ("21.5", [], []),
        ("21.6", [.. ComponentAccess.NewReadingSourceRows, .. SchemaColumns.NewReadingStoreRows, .. FixtureExpectations.NewReadingClaims, .. Reading.ReadSurface.NewReadingSourceClaims], []),
        ("21.6 correction", [], []),
        ("21.7", [.. Reading.ReadSurface.ChanceClaims], []),
        ("21.8", [.. FixtureExpectations.RankingClaims], []),
        ("21.9", [.. FixtureExpectations.ExposureClaims, .. Reading.ReadSurface.HalvedClaims], []),
        ("21.10", [.. FixtureExpectations.VersionChoiceClaims, .. Reading.ReadSurface.VersionChoicePageClaims], []),
        ("21.16", [], []),
        ("21.11", [.. Reading.ReadSurface.CombinedPageClaims], []),
        ("21.12", [.. FixtureExpectations.HousekeepingClaims], []),
        ("21.13", [.. Reading.ReadSurface.StripRows], []),
        ("21.14", [.. FixtureExpectations.ExitFillRows, .. Reading.ReadSurface.ToneLineClaims], []),
        ("21.15", [], []),
    ];

    // The rows the document gains after phase 21's report, named beside the pair and never counted in it: none yet.
    internal static string[] AfterPhaseTwentyOne => [];

    // The rows the document loses after phase 21's report: none.
    internal static string[] TakenOutAfterPhaseTwentyOne => [];

    // How many more rows each checkpoint landed than its count in the plan, a negative figure where it landed fewer. 21.0,
    // none as counted. 21.1, none of two: the per-rule test's rows in section 18 and 15.21 were amended in place. 21.2,
    // none as counted. 21.3, two of eight: the three commands' catalogue and matrix rows landed as the score audit's two,
    // the exits check and the open floor being the setup ledger's and the walk-forward tester's commands, and their
    // report rows as paragraphs. 21.4, three of one, the hooks' rows amended in place and the exposure's, the ranking's and
    // the chosen readings' rows added; its correction one, which no count held. 21.5, none of eight: every row it writes
    // was amended in place. 21.6, fifteen of nine: the three components' rows, where the plan counted one process's. 21.7,
    // one of three. 21.8, one of two. 21.9, two of three. 21.10, two of four. 21.16, added on the operator's ruling after
    // the plan counted, none. 21.11, three of seven. 21.12, one of three. 21.13, two of three: the market strip's two
    // rows, the operator's addition, where its plain words were placed whole. 21.14, three of none. 21.15, none of one.
    static readonly (string Checkpoint, int More)[] PhaseTwentyOneMore =
    [
        ("21.0", 0),
        ("21.1", -2),
        ("21.1 correction", 0),
        ("21.2", 0),
        ("21.3", -6),
        ("21.4", 2),
        ("21.4 correction", 1),
        ("21.5", -8),
        ("21.6", 6),
        ("21.6 correction", 0),
        ("21.7", -2),
        ("21.8", -1),
        ("21.9", -1),
        ("21.10", -2),
        ("21.16", 0),
        ("21.11", -4),
        ("21.12", -2),
        ("21.13", -1),
        ("21.14", 3),
        ("21.15", -1),
    ];

    // The plan's counts add to four more than the pair it states, 54 against its about 50; the pair is checked as stated
    // and the counts as stated, the four between them named here.
    const int PlanCountsOverPair = 4;

    [Fact]
    public void ThePhaseTwentyOnePairIsCheckedAgainstTheActualWithEveryClaimThatMovedNamed()
    {
        // The pair and each checkpoint's count read off the plan, so the figures checked are the ones it carries.
        const string pair = @"([\d,]+) claims and \1 PASS on main at [0-9a-f]{8}\. After 21\.15 about ([\d,]+), within ([\d,]+) to ([\d,]+):\s*- none at 21\.0, [^;]*;\s*- about (\d+) at 21\.1, [^;]*;\s*- none at 21\.2;\s*- about (\d+) at 21\.3, [^;]*;\s*- about (\d+) net at 21\.4, [^;]*;\s*- about (\d+) at 21\.5, [^;]*;\s*- about (\d+) at 21\.6, [^;]*;\s*- about (\d+) at 21\.7, [^;]*;\s*- about (\d+) at 21\.8;\s*- about (\d+) at 21\.9;\s*- about (\d+) at 21\.10;\s*- about (\d+) at 21\.11, [^;]*;\s*- about (\d+) at 21\.12;\s*- about (\d+) at 21\.13, [^;]*;\s*- none at 21\.14;\s*- about (\d+) at 21\.15\.";
        var stated = Regex.Match(Corpus.Read("docs/BUILD_PLAN.md"), pair.Replace(" ", @"\s+", StringComparison.Ordinal));

        Assert.True(stated.Success, "The plan states no pair for phase 21.");

        int Figure(int group) => int.Parse(stated.Groups[group].Value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture);

        var (before, predicted, low, high) = (Figure(1), Figure(2), Figure(3), Figure(4));
        var byCheckpoint = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["21.1"] = Figure(5), ["21.3"] = Figure(6), ["21.4"] = Figure(7), ["21.5"] = Figure(8), ["21.6"] = Figure(9),
            ["21.7"] = Figure(10), ["21.8"] = Figure(11), ["21.9"] = Figure(12), ["21.10"] = Figure(13), ["21.11"] = Figure(14),
            ["21.12"] = Figure(15), ["21.13"] = Figure(16), ["21.15"] = Figure(17),
        };
        var landed = PhaseTwentyOneLanded;
        int[] counted = [.. landed.Select(part => byCheckpoint.GetValueOrDefault(part.Checkpoint))];

        Assert.Equal(landed.Select(part => part.Checkpoint), PhaseTwentyOneMore.Select(part => part.Checkpoint));

        // What each checkpoint landed: its count in the plan and the rows it landed beyond it.
        int Net((string Checkpoint, string[] Added, string[] Taken) part) => part.Added.Length - part.Taken.Length;

        Assert.Equal(counted.Select((count, at) => count + PhaseTwentyOneMore[at].More), landed.Select(Net));

        // The figure the pair starts from: phase 22's actual and the rows 22.13 and 22.14 landed after phase 22's report,
        // which are the rows phase 22's pair named after it less phase 21's own.
        var rows = landed.SelectMany(part => part.Added).ToArray();
        var lateTwentyTwo = AfterPhaseTwentyTwo.Except(rows, StringComparer.Ordinal).ToArray();

        Assert.Equal(1444 + lateTwentyTwo.Length, before);
        Assert.Equal(
            AfterPhaseTwentyTwo.Except(AfterPhaseTwentyOne, StringComparer.Ordinal).Except(lateTwentyTwo, StringComparer.Ordinal).Order(StringComparer.Ordinal),
            rows.Order(StringComparer.Ordinal));

        // The pair the plan states and the counts it states, the four between them named.
        Assert.Equal(predicted, before + counted.Sum() - PlanCountsOverPair);

        // The actual stands inside the range the plan stated, short of the pair by the rows each checkpoint landed fewer
        // than its count, named above, less the four the counts held over the pair.
        var actual = before + landed.Sum(Net);

        Assert.Equal(predicted + PlanCountsOverPair + PhaseTwentyOneMore.Sum(part => part.More), actual);
        Assert.InRange(actual, low, high);

        // Every row the phase added reached once and passing, and the report holding the phase's rows over what stood
        // before it, none out of scope and none unexamined.
        var report = Report();
        var total = actual + AfterPhaseTwentyOne.Length - TakenOutAfterPhaseTwentyOne.Length;

        Assert.Equal(rows.Length, rows.Distinct(StringComparer.Ordinal).Count());
        Assert.All(rows, key => Assert.Equal(Verdict.Pass, Assert.Single(report.Claims, claim => CheckReach.Key(claim.Table, claim.Subject) == key).Verdict));
        Assert.Equal(
            (total, 0, 0, total),
            (report.Claims.Count, report.Count(Verdict.OutOfScope), report.Count(Verdict.Unexamined), report.Count(Verdict.Pass)));

        // Stated, so a claim added or lost without being named here moves this rather than the sum.
        Assert.Equal((1455, 1505, 1485, 1525, 1491), (before, predicted, low, high, actual));
    }
}
