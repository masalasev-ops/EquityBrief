using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Checks;

// architecture-conformance, phase 13: the rows the setup families add to the document, named by the
// checkpoint that adds them, so the pairs the earlier phases predicted are read against a count that
// says where every later row came from.
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
}
