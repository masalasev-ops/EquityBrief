using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Checks;

// architecture-conformance, 17.10: phase 17's pair read off the plan and checked against the actual, every row the phase
// added reached once and passing, and each checkpoint's count against the rows it landed with where every difference went.
public partial class ArchitectureConformance
{
    // The rows of phase 17 each checkpoint landed, in the plan's order: 17.1's searches and pulls, 17.2's cards, none at
    // 17.2a, 17.3's kept bars, ledger, filings refresh and Ledger page, 17.4's tester and Loop page, 17.5's autopsy and the
    // heavyweights' card's selector, 17.6's conditions, 17.7's score, 17.8's family, 17.9's approval and alarm, and 17.10's
    // monthly run.
    static (string Checkpoint, string[] Rows)[] PhaseSeventeenLanded =>
    [
        ("17.1", FixtureExpectations.SearchClaims),
        ("17.2", FixtureExpectations.RuleCardsRows),
        ("17.2a", []),
        ("17.3", [CheckReach.Key(Scope.StoresTable, "Kept bars"), .. FixtureExpectations.LedgerRows, .. NightlyCost.FiledFactsRows, .. Reading.ReadSurface.LedgerPageRows]),
        ("17.4", [.. FixtureExpectations.LoopRows, CheckReach.Key(Scope.LoopPage, Scope.LoopRuleToday), CheckReach.Key(Scope.LoopPage, Scope.LoopVerdict), CheckReach.Key(Scope.LoopPage, Scope.LoopTestYears)]),
        ("17.5", [.. FixtureExpectations.AutopsyRows, CheckReach.Key(Scope.LoopPage, Scope.LoopFindings), .. Reading.ReadSurface.HeavyweightSelectorRows]),
        ("17.6", [.. FixtureExpectations.ConditionRows, CheckReach.Key(Scope.LoopPage, Scope.LoopReadings)]),
        ("17.7", FixtureExpectations.ScoreRows),
        ("17.8", [.. FixtureExpectations.FundamentalsClaims, .. Reading.ReadSurface.FundamentalsPageClaims]),
        ("17.9", FixtureExpectations.ApprovalRows),
        ("17.10", FixtureExpectations.MonthlyRows),
    ];

    // How many fewer rows each checkpoint landed than its count in the plan, a negative figure where it landed more, and
    // what it landed. 17.1, six as counted. 17.2, twenty-four: the rule cards' catalogue and matrix rows, three stores,
    // section 17's two rows and section 18's two, the card's rule as fourteen parts and the checklist's item on a live
    // rule past its mark, four more. 17.2a is not built, waiting on the operator's approval of the store's growth
    // proposal, its three not landed. 17.3, twenty-three: the kept bars, the ledger's nine, the filings refresh's nine and
    // the Ledger page's four, one more. 17.4, fifteen: the tester's catalogue and matrix rows, three stores, section 17's
    // five rows, section 18's two and the Loop page's three regions, three more. 17.5, six: the findings' store, section
    // 17's two rows and section 18's one, the Loop page's findings and the heavyweights' card's selector, two fewer. 17.6,
    // five as counted. 17.7, eight: the models' store, section 17's three rows, section 18's two, the card's part and the
    // Loop page's score, four fewer. 17.8, four: section 17's two rows, section 18's one and the family's card, eight
    // fewer. 17.9, eighteen: the apply step's and the alarm's catalogue and matrix rows, five stores, section 17's two
    // rows, section 18's two, the Loop page's three regions, Tonight's line and the card's row, four more. 17.10, four as
    // counted.
    static readonly (string Checkpoint, int Fewer)[] PhaseSeventeenShort =
    [
        ("17.1", 0),
        ("17.2", -4),
        ("17.2a", 3),
        ("17.3", -1),
        ("17.4", -3),
        ("17.5", 2),
        ("17.6", 0),
        ("17.7", 4),
        ("17.8", 8),
        ("17.9", -4),
        ("17.10", 0),
    ];

    // The rows the document gains after phase 17's report, named beside the pair and never counted in it: the 3.1
    // correction's chart averages, the 16.3 correction's dividend step, and phase 18's rows as each of its checkpoints
    // lands them, which its own report counts.
    internal static string[] AfterPhaseSeventeen => [.. NightlyRun.ChartAverageRows, .. FixtureExpectations.DividendStepClaims, .. PhaseEighteenRows];

    // The rows phase 18 lands, by checkpoint: 18.1's quote job, its two stores, the day's cap and its three failures, and
    // the name page's parts, the range bar, the Run page's quote runs and the interval.
    internal static string[] PhaseEighteenRows =>
    [
        .. ComponentAccess.QuoteJobRows,
        .. SchemaColumns.QuoteStoreRows,
        .. FixtureExpectations.QuoteRows,
        .. Reading.ReadSurface.NamePageRows,
    ];

    // The rows the document loses after phase 17's report, each counted by the report of the phase that added it: the
    // 6.10 correction's overnight queue, its catalogue and matrix rows, its step, section 17's row, section 18's row for
    // a night the machine slept and the part of the local model's row the queue recorded, and the run page's region.
    // The step is named by how it opened, since its whole text cites the decisions its retirement superseded.
    internal static readonly string[] TakenOutAfterPhaseSeventeen =
    [
        CheckReach.Key(Scope.CatalogueTable, "Overnight queue"),
        CheckReach.Key(Scope.MatrixTable, "Overnight queue"),
        CheckReach.Key(NightlyRunSteps.Heading, "Run the overnight queue"),
        CheckReach.Key(Scope.LimitsTable, "Overnight queue"),
        CheckReach.Key(Scope.FailureTable, "The machine slept and the overnight queue did not run"),
        CheckReach.Key(Scope.FailureTable, "The local model is unavailable, the overnight queue records that it could not run"),
        CheckReach.Key("15.10 Run", "Overnight queue"),
    ];

    [Fact]
    public void EachRowTheQueuesRetirementTookOutIsNoClaimAndEveryOtherClaimStands()
    {
        // Each taken out row is in no claim's key, the step by the words it opened on, and the claims now are the
        // report's after phase 17 with the chart averages' rows and the dividend step added and these taken out.
        var keys = Report().Claims.Select(claim => CheckReach.Key(claim.Table, claim.Subject)).ToArray();

        Assert.All(TakenOutAfterPhaseSeventeen, taken => Assert.DoesNotContain(keys, key => key == taken || key.StartsWith(taken + " ", StringComparison.Ordinal)));
        Assert.All(AfterPhaseSeventeen, added => Assert.Contains(added, keys));
        Assert.Equal(1149 + AfterPhaseSeventeen.Length - TakenOutAfterPhaseSeventeen.Length, keys.Length);
    }

    [Fact]
    public void ThePhaseSeventeenPairIsCheckedAgainstTheActualWithEveryClaimThatMovedNamed()
    {
        // The pair and each checkpoint's count read off the plan, so the figures checked are the ones it carries.
        const string pair = @"([\d,]+) claims and \1 PASS before it\. After 17\.10 the pair is ([\d,]+) and \2, within ([\d,]+) to ([\d,]+): none at 17\.0, [^;]*; about (\d+) at 17\.1, [^;]*; about (\d+) at 17\.2, [^;]*; about (\d+) at 17\.2a; about (\d+) at 17\.3, [^;]*; about (\d+) at 17\.4; about (\d+) at 17\.5; about (\d+) at 17\.6; about (\d+) at 17\.7; about (\d+) at 17\.8; about (\d+) at 17\.9; and about (\d+) at 17\.10,";
        var stated = Regex.Match(Corpus.Read("docs/BUILD_PLAN.md"), pair.Replace(" ", @"\s+", StringComparison.Ordinal));

        Assert.True(stated.Success, "The plan states no pair for phase 17.");

        int Figure(int group) => int.Parse(stated.Groups[group].Value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture);

        var (before, predicted, low, high) = (Figure(1), Figure(2), Figure(3), Figure(4));
        int[] counted = [.. Enumerable.Range(5, 11).Select(Figure)];

        Assert.Equal(predicted, before + counted.Sum());

        // What 17.1 to 17.10 landed: each checkpoint's count in the plan less the claims it landed fewer, or more.
        var landed = PhaseSeventeenLanded;

        Assert.Equal(landed.Select(checkpoint => checkpoint.Checkpoint), PhaseSeventeenShort.Select(checkpoint => checkpoint.Checkpoint));
        Assert.Equal(
            counted.Select((count, at) => count - PhaseSeventeenShort[at].Fewer),
            landed.Select(checkpoint => checkpoint.Rows.Length));

        var rows = landed.SelectMany(checkpoint => checkpoint.Rows).ToArray();
        var actual = before + rows.Length;

        // The actual falls inside the range the plan stated, short of the pair by the claims named above.
        Assert.InRange(actual, low, high);
        Assert.Equal(predicted - actual, PhaseSeventeenShort.Sum(checkpoint => checkpoint.Fewer));

        // Every row the phase added reached once and passing, and the report holding the phase's rows over what stood
        // before it and the rows after its report, none out of scope and none unexamined.
        var report = Report();
        var total = actual + AfterPhaseSeventeen.Length - TakenOutAfterPhaseSeventeen.Length;

        Assert.Equal(rows.Length, rows.Distinct(StringComparer.Ordinal).Count());
        Assert.All(rows, key => Assert.Equal(Verdict.Pass, Assert.Single(report.Claims, claim => CheckReach.Key(claim.Table, claim.Subject) == key).Verdict));
        Assert.Equal(
            (total, 0, 0, total),
            (report.Claims.Count, report.Count(Verdict.OutOfScope), report.Count(Verdict.Unexamined), report.Count(Verdict.Pass)));

        // Stated, so a claim added or lost without being named here moves this rather than the sum.
        Assert.Equal((1036, 1154, 1115, 1195, 113, 1149), (before, predicted, low, high, rows.Length, actual));
    }
}
