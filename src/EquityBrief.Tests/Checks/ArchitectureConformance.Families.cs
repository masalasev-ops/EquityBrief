using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Checks;

// architecture-conformance, phase 13: the rows the setup families add to the document, named by the
// checkpoint that adds them, so the pairs the earlier phases predicted are read against a count that
// says where every later row came from, and phase 13's own pair read off the plan and checked against
// the actual.
public partial class ArchitectureConformance
{
    // 13.1, the family framework: the lister's catalogue and matrix rows, its two stores, section 17's
    // five a family, the card tonight's page draws a family as the parts its row enumerates, and section
    // 18's three rows.
    internal static readonly string[] FamilyFrameworkClaims =
    [
        CheckReach.Key(Scope.CatalogueTable, "Family lister"),
        CheckReach.Key(Scope.MatrixTable, "Family lister"),
        CheckReach.Key(Scope.StoresTable, "Family nights"),
        CheckReach.Key(Scope.StoresTable, "Family picks"),
        CheckReach.Key(Scope.LimitsTable, "Names a family lists"),
        .. Reading.ReadSurface.FamilyCardClaims,
        .. Reading.ReadSurface.TwoFamilyClaims,
    ];

    // 13.2, breakouts: the family evaluator's catalogue and matrix rows, its store, section 17's rows for
    // the breakout's settings and section 18's two rows.
    internal static readonly string[] BreakoutFamilyClaims =
    [
        CheckReach.Key(Scope.CatalogueTable, "Family evaluator"),
        CheckReach.Key(Scope.MatrixTable, "Family evaluator"),
        CheckReach.Key(Scope.StoresTable, "Family results"),
        .. FixtureExpectations.BreakoutClaims,
    ];

    // 13.9, the freezes: the family recorder's catalogue and matrix rows and its store, section 17's rows for
    // the pullback's base and a family rule's list, section 18's three rows, and the run page's records.
    internal static readonly string[] FreezeClaims =
    [
        CheckReach.Key(Scope.CatalogueTable, "Family recorder"),
        CheckReach.Key(Scope.MatrixTable, "Family recorder"),
        CheckReach.Key(Scope.StoresTable, "Family trades"),
        .. FixtureExpectations.FamilyRecordClaims,
        .. Reading.ReadSurface.FamilyRecordPageClaims,
    ];

    // Every row phase 13 has added, in the order its checkpoints add them: the framework's, the breakout's,
    // the settings and failure rows of the earnings drift and of the sector leaders, the parts of the pages
    // around the families, the sweeps' rows and the freezes'.
    internal static readonly string[] PhaseThirteenRows =
    [
        .. FamilyFrameworkClaims,
        .. BreakoutFamilyClaims,
        .. FixtureExpectations.DriftClaims,
        .. FixtureExpectations.LeaderClaims,
        .. Reading.ReadSurface.FamilyPagesClaims,
        .. FixtureExpectations.FamilySweepClaims,
        .. FixtureExpectations.DriftSweepClaims,
        .. FixtureExpectations.LeaderSweepClaims,
        .. FreezeClaims,
    ];

    // The rows of phase 13 the record does not yet reach: each is placed at the checkpoint that draws it
    // and reads as out of scope until that checkpoint's entry lands, which an earlier phase's pair counts
    // beside its own figures and never among them.
    static int PhaseThirteenPending(PhaseReportModel report) =>
        report.Claims.Count(claim => claim.Verdict == Verdict.OutOfScope && PhaseThirteenRows.Contains(CheckReach.Key(claim.Table, claim.Subject)));

    // The rows the document gained after phase 13, each named where it was added: the ideas' run, a 12.5
    // correction built once the phase was finished, the store's copy, the operator's ruling of 2026-10-02, the
    // market switches, the operator's ruling of 2026-10-03, a drain's stop, the 9.2 correction of 2026-10-03,
    // 14.1's replay guarding a family rule's record, and 14.2's pulls and the readings over them.
    // 14.3's rows, whichever check reaches each: section 17's four, section 18's four and the fixture's row for the
    // members' companies, the book's catalogue and matrix rows, its two stores and each member's company, and the
    // pages' parts. Read on each use, since the lists that sum it stand in other files of this class. 14.4's context
    // checks add section 17's three rows and section 18's two, and 14.5's heavyweights' sweep section 17's row and section
    // 18's two.
    internal static string[] HeavyweightRows =>
    [
        .. FixtureExpectations.HeavyweightClaims,
        CheckReach.Key(Scope.CatalogueTable, "Heavyweight book"),
        CheckReach.Key(Scope.MatrixTable, "Heavyweight book"),
        CheckReach.Key(Scope.StoresTable, "Heavyweight nights"),
        CheckReach.Key(Scope.StoresTable, "Heavyweight holdings"),
        CheckReach.Key(Scope.StoresTable, "Member companies"),
        .. Reading.ReadSurface.HeavyweightPageClaims,
    ];

    // 14.6's rows, the freezes and registrations: section 17's two and section 18's two, the estimates fetcher's
    // catalogue and matrix rows, each registered heavyweights rule's two stores and the estimates the night asked for,
    // and the run page's part for the heavyweights' records.
    internal static string[] RegistrationRows =>
    [
        .. FixtureExpectations.HeavyweightFreezeClaims,
        CheckReach.Key(Scope.CatalogueTable, "Estimates fetcher"),
        CheckReach.Key(Scope.MatrixTable, "Estimates fetcher"),
        CheckReach.Key(Scope.StoresTable, "Heavyweight rule nights"),
        CheckReach.Key(Scope.StoresTable, "Heavyweight rule holdings"),
        CheckReach.Key(Scope.StoresTable, "Estimate readings"),
        .. Reading.ReadSurface.HeavyweightRecordPageClaims,
    ];

    internal static readonly string[] AfterPhaseThirteen = [.. FixtureExpectations.IdeasClaims, .. FixtureExpectations.StoreCopyRows, .. FixtureExpectations.MarketSwitchRows, .. Reading.ReadSurface.DrainStopRows, .. FixtureExpectations.FamilyReplayRows, .. FixtureExpectations.CompanyPullRows, .. HeavyweightRows, .. FixtureExpectations.ContextClaims, .. FixtureExpectations.HeavyweightSweepClaims, .. RegistrationRows, .. AfterPhaseFourteen];

    // The rows of phase 14 each checkpoint landed, in the plan's order, read on each use since the lists that hold them
    // stand in other files of this class.
    static (string Checkpoint, string[] Rows)[] PhaseFourteenLanded =>
    [
        ("14.1", FixtureExpectations.FamilyReplayRows),
        ("14.2", FixtureExpectations.CompanyPullRows),
        ("14.3", HeavyweightRows),
        ("14.4", FixtureExpectations.ContextClaims),
        ("14.5", FixtureExpectations.HeavyweightSweepClaims),
        ("14.6", RegistrationRows),
    ];

    // Where a checkpoint landed more or fewer claims than the plan counted for it, the rows its count did not name and
    // how many of them the count held, as the entry that landed them says. At 14.1 the replay's catalogue and matrix
    // rows and the setup families' two parts, four where the command's row and the run page's line held two. At 14.2
    // every row: the four pulled stores joined section 16's pulled history row and took none of their own, and section
    // 17's five rows and section 18's five landed, the fifth at the correction, ten where about eight held the stores
    // and the readings. At 14.3 the members' companies' store, the fixture's row, a name's line and Past picks' eleven
    // parts, fourteen the plan's sentence did not name, of which its about thirty-two held seven. At 14.4 every row,
    // section 17's three and section 18's two, five where about six counted a row an idea. And at 14.6 Part B's rows,
    // section 17's estimates raised, section 18's estimates not served, the estimates fetcher's catalogue and matrix rows
    // and the estimate readings, five the plan's sentence did not name, of which its about eight held three.
    static (string Checkpoint, string[] Rows, int Counted)[] PhaseFourteenMoved =>
    [
        ("14.1", [CheckReach.Key(Scope.CatalogueTable, "Family replay"), CheckReach.Key(Scope.MatrixTable, "Family replay"), .. Reading.ReadSurface.FamilyReplayPageClaims], 2),
        ("14.2", FixtureExpectations.CompanyPullRows, 8),
        ("14.3",
        [
            CheckReach.Key(Scope.StoresTable, "Member companies"),
            CheckReach.Key(Scope.FixtureTable, "member companies"),
            CheckReach.Key("15.9 Name", "Held by the sector heavyweights"),
            .. Reading.ReadSurface.HeavyweightPageClaims.Where(key => key.StartsWith(CheckReach.Key("15.17 Past picks", ""), StringComparison.Ordinal)),
        ], 7),
        ("14.4", FixtureExpectations.ContextClaims, 6),
        ("14.6",
        [
            CheckReach.Key(Scope.LimitsTable, "Estimates raised"),
            CheckReach.Key(Scope.FailureTable, "The provider does not serve a member's estimates on the night"),
            CheckReach.Key(Scope.CatalogueTable, "Estimates fetcher"),
            CheckReach.Key(Scope.MatrixTable, "Estimates fetcher"),
            CheckReach.Key(Scope.StoresTable, "Estimate readings"),
        ], 3),
    ];

    // The rows the document gains after phase 14's report, named beside the pair and never counted in it: 14.8's,
    // section 17's row for the wider universe's test and section 18's two, from 15.1 section 18's row for a fund's file
    // the night cannot read, from 15.2 the readings' rows, from 15.3 section 18's two on the funds' holdings, and from
    // 15.1's second half the index families' catalogue and matrix rows, from 15.4 the sweep answers' catalogue and
    // matrix rows, from 15.2's second half the member reader's and the name page's member readings, from 15.5 the
    // index rule trades' store and section 18's row on a registered rule the night does not read, and every row after
    // phase 15's report.
    internal static string[] AfterPhaseFourteen => [.. FixtureExpectations.WiderUniverseClaims, .. NightlyRun.ThreeIndicesRows, .. FixtureExpectations.ReadingsClaims, .. FixtureExpectations.HoldingsClaims, .. IndexFamiliesRows, .. SweepAnswersRows, .. MemberReadingRows, .. IndexRuleRows, .. AfterPhaseFifteen];

    // 15.5's rows: the index rule trades' store and section 18's row on a registered rule the night does not read, and
    // from its second pull request each heavyweights rule's book's two stores and section 18's row on its rebalance
    // waiting for what its design reads.
    internal static string[] IndexRuleRows =>
    [
        CheckReach.Key(Scope.StoresTable, "Index rule trades"),
        .. FixtureExpectations.IndexFreezeClaims,
        CheckReach.Key(Scope.StoresTable, "Index heavyweight rule nights"),
        CheckReach.Key(Scope.StoresTable, "Index heavyweight rule holdings"),
        .. FixtureExpectations.IndexHeavyweightFreezeClaims,
    ];

    // The sweep answers' catalogue and matrix rows, 15.4.
    internal static string[] SweepAnswersRows =>
    [
        CheckReach.Key(Scope.CatalogueTable, "Sweep answers"),
        CheckReach.Key(Scope.MatrixTable, "Sweep answers"),
    ];

    // 15.2's second half: the member reader's catalogue and matrix rows, the two stores, section 17's row, section 18's
    // row, the fixture's row, the name page's member readings and the run page's two parts.
    internal static string[] MemberReadingRows =>
    [
        CheckReach.Key(Scope.CatalogueTable, "Member reader"),
        CheckReach.Key(Scope.MatrixTable, "Member reader"),
        CheckReach.Key(Scope.StoresTable, "Member readings"),
        CheckReach.Key(Scope.StoresTable, "Switch readings"),
        .. FixtureExpectations.MemberReadingExpectationClaims,
        .. Reading.ReadSurface.MemberReadingClaims,
    ];

    // The index families' catalogue and matrix rows, 15.1's second half.
    internal static string[] IndexFamiliesRows =>
    [
        CheckReach.Key(Scope.CatalogueTable, "Index families"),
        CheckReach.Key(Scope.MatrixTable, "Index families"),
    ];

    [Fact]
    public void ThePhaseFourteenPairIsCheckedAgainstTheActualWithEveryClaimThatMovedNamed()
    {
        // The pair and each checkpoint's count read off the plan, so the figures checked are the ones it carries.
        const string pair = @"(\d+) claims and \1 PASS before it\. After 14\.6 the pair is (\d+) and \2, within (\d+) to (\d+): none at 14\.0, [^;]*; about (\d+) at 14\.1, [^;]*; about (\d+) at 14\.2, [^;]*; about (\d+) at 14\.3, [^;]*; about (\d+) at 14\.4, [^;]*; about (\d+) at 14\.5, [^;]*; and about (\d+) at 14\.6,";
        var stated = Regex.Match(Corpus.Read("docs/BUILD_PLAN.md"), pair.Replace(" ", @"\s+", StringComparison.Ordinal));

        Assert.True(stated.Success, "The plan states no pair for phase 14.");

        int Figure(int group) => int.Parse(stated.Groups[group].Value, CultureInfo.InvariantCulture);

        var (before, predicted, low, high) = (Figure(1), Figure(2), Figure(3), Figure(4));
        int[] counted = [Figure(5), Figure(6), Figure(7), Figure(8), Figure(9), Figure(10)];

        Assert.Equal(predicted, before + counted.Sum());

        // What 14.1 to 14.6 landed: each checkpoint's count in the plan and the rows beyond it named above, each of them
        // a row its checkpoint landed.
        var landed = PhaseFourteenLanded;
        var moved = PhaseFourteenMoved.ToDictionary(row => row.Checkpoint, row => row.Rows.Length - row.Counted, StringComparer.Ordinal);

        Assert.All(PhaseFourteenMoved, row => Assert.All(row.Rows, key => Assert.Contains(key, landed.Single(checkpoint => checkpoint.Checkpoint == row.Checkpoint).Rows)));
        Assert.Equal(
            counted.Select((count, at) => count + moved.GetValueOrDefault(landed[at].Checkpoint)),
            landed.Select(checkpoint => checkpoint.Rows.Length));

        var rows = landed.SelectMany(checkpoint => checkpoint.Rows).ToArray();
        var actual = before + rows.Length;

        Assert.InRange(actual, low, high);

        // Every row the phase added reached once and passing, and the report holding the phase's rows over what stood
        // before it and the rows after it, none out of scope and none unexamined.
        var report = Report();
        var total = actual + AfterPhaseFourteen.Length - TakenOutAfterPhaseSeventeen.Length;

        // A row a later checkpoint took out, as 19.3 took out two parts of the heavyweights card 14.3 landed, is counted
        // where it landed and is in no claim now.
        Assert.Equal(rows.Length, rows.Distinct(StringComparer.Ordinal).Count());
        Assert.All(rows.Except(TakenOutAfterPhaseSeventeen), key => Assert.Equal(Verdict.Pass, Assert.Single(report.Claims, claim => CheckReach.Key(claim.Table, claim.Subject) == key).Verdict));
        Assert.All(rows.Intersect(TakenOutAfterPhaseSeventeen), key => Assert.DoesNotContain(report.Claims, claim => CheckReach.Key(claim.Table, claim.Subject) == key));
        Assert.Equal(
            (total, 0, 0, total),
            (report.Claims.Count, report.Count(Verdict.OutOfScope), report.Count(Verdict.Unexamined), report.Count(Verdict.Pass)));

        // Stated, so a claim added or lost without being named here moves this rather than the sum.
        Assert.Equal((898, 958, 940, 975, 72, 970), (before, predicted, low, high, rows.Length, actual));
    }

    // Where a checkpoint landed more claims than the plan counted for it, the rows it landed more of and how
    // many of them the plan's count held, as the entry that landed them says: at 13.2 section 17's rows for the
    // breakout's settings, six where the plan's about ten held five beside its store, its component's two rows
    // and its two failure rows; and at 13.5 Past picks' rows, four where the plan named three, its setup filter,
    // its label and its trailing trades, the fourth saying a provisional setup's trades are counted in no share,
    // which the plan did not state.
    static readonly (string Checkpoint, string[] Rows, int Counted)[] PhaseThirteenMoved =
    [
        ("13.2", [.. FixtureExpectations.BreakoutClaims.Where(key => key.StartsWith(CheckReach.Key(Scope.LimitsTable, ""), StringComparison.Ordinal))], 5),
        ("13.5", [.. Reading.ReadSurface.FamilyPagesClaims.Where(key => key.StartsWith(CheckReach.Key("15.17 Past picks", ""), StringComparison.Ordinal))], 3),
    ];

    [Fact]
    public void ThePhaseThirteenPairIsCheckedAgainstTheActualWithEveryClaimThatMovedNamed()
    {
        // The pair and each checkpoint's count read off the plan, so the figures checked are the ones it carries.
        const string pair = @"(\d+) claims and \1 PASS before it\. After 13\.5 the pair is (\d+) and \2, within (\d+) to (\d+): (\d+) at 13\.1, [^;]*; about (\d+) at 13\.2, [^;]*; about (\d+) at 13\.3 and (\d+) at 13\.4, [^;]*; and about (\d+) at 13\.5,";
        var stated = Regex.Match(Corpus.Read("docs/BUILD_PLAN.md"), pair.Replace(" ", @"\s+", StringComparison.Ordinal));

        Assert.True(stated.Success, "The plan states no pair for phase 13.");

        int Figure(int group) => int.Parse(stated.Groups[group].Value, CultureInfo.InvariantCulture);

        var (before, predicted, low, high) = (Figure(1), Figure(2), Figure(3), Figure(4));
        int[] counted = [Figure(5), Figure(6), Figure(7), Figure(8), Figure(9)];

        Assert.Equal(predicted, before + counted.Sum());

        // What 13.1 to 13.5 landed: each checkpoint's count in the plan and the rows beyond it named above.
        (string Checkpoint, string[] Rows)[] landed =
        [
            ("13.1", FamilyFrameworkClaims),
            ("13.2", BreakoutFamilyClaims),
            ("13.3", FixtureExpectations.DriftClaims),
            ("13.4", FixtureExpectations.LeaderClaims),
            ("13.5", Reading.ReadSurface.FamilyPagesClaims),
        ];

        var moved = PhaseThirteenMoved.ToDictionary(row => row.Checkpoint, row => row.Rows.Length - row.Counted, StringComparer.Ordinal);

        Assert.Equal(
            counted.Select((count, at) => count + moved.GetValueOrDefault(landed[at].Checkpoint)),
            landed.Select(checkpoint => checkpoint.Rows.Length));

        var actual = before + landed.Sum(checkpoint => checkpoint.Rows.Length);

        Assert.InRange(actual, low, high);

        // The sweeps' rows and the freezes' came after the pair, which counts nothing past 13.5.
        var now = actual
            + FixtureExpectations.FamilySweepClaims.Length
            + FixtureExpectations.DriftSweepClaims.Length
            + FixtureExpectations.LeaderSweepClaims.Length
            + FreezeClaims.Length;

        // Every row the phase added reached once and passing, and the rows added after it named apart.
        var report = Report();
        var total = now + AfterPhaseThirteen.Length - TakenOutAfterPhaseSeventeen.Length;

        // A row a later checkpoint took out, as 19.3 took out card parts 13.1 and 13.5 landed, is counted where it landed
        // and is in no claim now.
        Assert.Equal(PhaseThirteenRows.Length, PhaseThirteenRows.Distinct(StringComparer.Ordinal).Count());
        Assert.All(PhaseThirteenRows.Except(TakenOutAfterPhaseSeventeen), key => Assert.Equal(Verdict.Pass, Assert.Single(report.Claims, claim => CheckReach.Key(claim.Table, claim.Subject) == key).Verdict));
        Assert.All(PhaseThirteenRows.Intersect(TakenOutAfterPhaseSeventeen), key => Assert.DoesNotContain(report.Claims, claim => CheckReach.Key(claim.Table, claim.Subject) == key));
        Assert.All(AfterPhaseThirteen.Except(TakenOutAfterPhaseSeventeen), key => Assert.Contains(report.Claims, claim => CheckReach.Key(claim.Table, claim.Subject) == key));

        Assert.Equal(
            (total, 0, 0, total),
            (report.Claims.Count, report.Count(Verdict.OutOfScope), report.Count(Verdict.Unexamined), report.Count(Verdict.Pass)));

        // Stated, so a claim added or lost without being named here moves this rather than the sum. 975 from 15.1, its
        // fund's file row and the Run page's members, 979 from 15.2's first half, section 17's three readings and
        // section 18's company filing no dated income, 981 from 15.3, section 18's filing the history pull cannot
        // read and holding matched to no code, 983 from 15.1's second half, the index families' catalogue and matrix
        // rows, 985 from 15.4, the sweep answers' catalogue and matrix rows, 995 from 15.2's second half, the member
        // reader's catalogue and matrix rows, its two stores, section 17's and section 18's rows, the fixture's, the name
        // page's member readings and the run page's two parts, 997 from 15.5, the index rule trades' store and section
        // 18's row on a registered rule the night does not read, 1000 from its second pull request, the heavyweights
        // rules' books' two stores and section 18's row on a rebalance waiting for what its design reads, 1014 from
        // 16.1, the decision cards' and the rule recorder's catalogue and matrix rows, their two stores, section 17's four
        // values and section 18's four failures, 1016 from the 16.1 ruling's local model, section 18's rows on its load
        // and on its settings, 1021 from 16.2, the taken trades' store, section 17's two values and section 18's two
        // rows, 1031 from 16.3, the follower's catalogue and matrix rows, the operator's record's and the dividend
        // readings' stores, section 17's three values and section 18's three rows, 1032 from the 14.6 correction,
        // section 18's row on a heavyweights' rebalance waiting for the closes its readings need, 1036 from the 13.10
        // correction of 2026-10-08, the checklist's two items on the store's copies and section 18's two rows on a copy
        // ended before it finished and on copies gone, and 1042 from 17.1, section 17's five rows on the searches and
        // the pulls' provider stop and section 18's row on a pull past it, and 1066 from 17.2, the rule cards' catalogue
        // and matrix rows, its three stores, section 17's two rows, section 18's two, the card's rule row as its fourteen
        // parts and the checklist's item on a live rule past its mark, 1067 from 17.3's kept bars, 1076 from 17.3's
        // ledger, the setup ledger's catalogue and matrix rows, its two stores, section 17's two rows, section 18's two
        // and the night's step, 1085 from 17.3's filings refresh, the refresher's catalogue and matrix rows, its three
        // stores, section 17's row, section 18's two and the night's step, 1089 from 17.3's Ledger page, its three
        // regions and its summary's store, 1104 from 17.4, the tester's catalogue and matrix rows, its three stores,
        // section 17's five rows, section 18's two and the Loop page's three regions, 1110 from 17.5, the autopsy's store,
        // section 17's two rows, section 18's one, the Loop page's findings and the heavyweights' card's rule, 1115 from
        // 17.6, the readings' store, section 17's two rows, section 18's one and the Loop page's readings, 1123 from
        // 17.7, the models' store, section 17's three rows, section 18's two, the card's score part and the Loop page's
        // learned score, 1127 from 17.8, section 17's two rows on the fundamentals-first family and its search,
        // section 18's one and the family's card on Tonight, and 1145 from 17.9, the apply step's and the alarm's
        // catalogue and matrix rows, their five stores, section 17's two rows, section 18's two, the Loop page's three
        // regions, Tonight's alarm line and the card's row for a rule standing at an approved change, 1149 from 17.10,
        // the monthly run's catalogue and matrix rows, section 17's row and section 18's, 1154 from the 3.1
        // correction, the chart averages' night step, catalogue and matrix rows, store and the chart's part, 1147 from
        // the 6.10 correction, which took out the overnight queue's catalogue and matrix rows, its step, section 17's
        // row, section 18's row and part, and the run page's region, 1148 from the 16.3 correction, section 17's
        // row on the dividend's steps, 1167 from 18.1, the quote job's catalogue and matrix rows, its two stores,
        // section 17's two rows, section 18's three, the range bar, the name page's eight parts and the run page's
        // quote runs, 1199 from 18.2, the dividend keeper's and the Treasury reader's catalogue and matrix rows,
        // three stores, the Treasury's step, section 17's two rows, section 18's six, the name page's seven regions and
        // the nine marks, and 1235 from 18.3, the page history reader's catalogue and matrix rows, its two stores,
        // section 17's row, section 18's seven and the gap row's part, the name page's two regions, the four marks and
        // the parts the level chart, the chart, the plan, the reactions and the moves gain, less the twelve-month
        // picture and the reactions' old place, and 1266 from 18.4, section 17's two rows, section 18's three, the
        // headline as its four parts where it was one, the risks' two columns, the calendar and the sources as their
        // sixteen and the calendar timeline as its eight, less the dates and sources, 1267 from the 17.10 correction,
        // the checklist's item on the newest monthly run, 1268 from 19.1, the card's rule row's part on the variant
        // reading no market check, and 1277 from 19.2, its store of every member's readings, three marks, Tonight's two
        // rows and its date line's part, the card's and the stock's page's chart, the five rows moving from Tonight to
        // the Run page under new keys less the five they leave, 1283 from 19.3, seventeen parts of the setup cards and
        // the heavyweights card less the eleven they retire, and 1295 from 19.4, its two stores, section 18's rule the
        // per-rule test cannot walk, paper trading's seven rows and the trade log's two, and 1303 from 19.5, Universe's
        // tiles and its eight rows beside them, the Run page's corrections and section 17's two rows, less the sector
        // strip's four parts, and 1305 from 19.6, the Queue's reports written by hand and section 17's window, and 1306
        // from the second 19.4 correction of 2026-10-13, paper trading's start and the version an account counts, and 1307
        // from the 19.6 correction of 2026-10-14, the Queue's Write now press, and 1312 from 19.7, its twenty-four rows
        // and parts of the remaining screens less the nineteen it retires, and 1313 from 19.8, Tonight's card of the stocks
        // one gate short, and 1316 from 19.10 on the operator's rulings of 2026-10-15, the fill step's catalogue and matrix
        // rows and its store, and 1327 from 19.11 on the rulings of 2026-10-16, the chart page's ten rows and the Run page's
        // time of every page, and 1330 from 19.12 on the same rulings, which readings go with winners on the Ledger page,
        // its store and section 17's row, and 1337 from 19.13 on the operator's ruling of the same day, the insider trades'
        // catalogue and matrix rows, their two stores, section 17's row, section 18's and 15.9's region, and 1340 from the
        // 17.3 correction of 2026-10-16, the SEC tickers step's catalogue and matrix rows and its store, and 1349 from
        // 20.1, the local run's catalogue and matrix rows, the reading pins' store, section 17's row, section 18's four and
        // the Run page's local model region, and 1355 from 20.2, the release fetch's catalogue and matrix rows, its two
        // stores, section 17's row and section 18's, and 1362 from 20.3, the accuracy report's catalogue and matrix rows,
        // the three stores of the releases' filers, readings and probes, section 17's row and section 18's, and 1364 from
        // 20.4, the tone variant's card row in 15.7 and section 17's window row, and 1366 from 20.5, the local comparison's
        // catalogue and matrix rows, and 1372 from 20.9, the idea generator's catalogue and matrix rows, its store, the Loop
        // page's row and section 18's two, and 1378 from the 19.4 correction of 2026-10-22, paper trading's seven rows for
        // its parts less its table of every account, and 1379 from the 20.3 correction of the same day, the release
        // checks' store, and 1383 from 20.12, the chart page's graphs and their three marks, and 1396 from 22.1, the dip
        // family's catalogue and matrix rows, its four stores, section 17's row, section 18's four, its card on Tonight and
        // its trades on Past picks, and 1400 from 20.8, merged after them, the usefulness store, the Ledger page's and the
        // Loop page's rows and section 17's window row, and 1417 from 20.13, the analyst job's and the ratings job's
        // catalogue and matrix rows, their three stores, section 17's three rows, section 18's four, the target range and
        // the chart page's firm actions and analysts by name, and 1418 from the 20.13 correction of 2026-10-23, section
        // 17's row for the 15 days a stock's TipRanks press waits, and 1421 from 22.2, section 17's grid of the dip's
        // sweep, section 18's row for a market series it reads missing a session and the Ledger page's dip counts, and 1422
        // from 22.4, the chart page's brief read, and 1424 from 22.6, Tonight in one minute and the page's words at its foot,
        // and 1433 from 22.8, the fundamentals scorecard's catalogue and matrix rows, its store, section 17's numbers,
        // section 18's three and its card on the chart page and the name page.
        Assert.Equal(
            (789, 853, 6, 4, 855, 876, 1433),
            (before, predicted, PhaseThirteenMoved[0].Rows.Length, PhaseThirteenMoved[1].Rows.Length, actual, now, total));
    }
}
