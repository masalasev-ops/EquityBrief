using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Checks;

// architecture-conformance. Every claim a table in ARCHITECTURE.html makes has a
// verdict, and every table in the document is placed so none can go unread.
//
// This runs every CI run and asserts the shape of the report. Whether the
// verdicts are good enough to sign a phase off is tools/verify-phase's question,
// and it is a gate on a phase rather than on a commit.
public class ArchitectureConformance
{
    static PhaseReportModel Report()
    {
        var document = File.ReadAllText(Repository.Architecture);

        return PhaseReport.Build(
            ArchitectureTables.In(document),
            NightlyRunSteps.In(document),
            Fixtures.Of(Repository.Root));
    }

    [Fact]
    public void EveryTableInTheDocumentIsPlaced()
    {
        var tables = ArchitectureTables.In(File.ReadAllText(Repository.Architecture));

        // Scope, stated in advance, with a floor far enough below the count
        // that ordinary growth never moves it.
        Assert.True(tables.Count >= 20, $"Read {tables.Count} tables, expected at least 20.");

        // Build throws on a table that is neither a claim source nor placed, so
        // reaching this line is most of the assertion. What is left is that
        // every table parsed actually appears in the placement the report
        // carries, which is the surface a person reads it on.
        var placed = Report().Tables.Select(entry => entry.Heading).ToArray();

        Assert.DoesNotContain(tables, table => !placed.Contains(table.Heading, StringComparer.Ordinal));
    }

    [Fact]
    public void EveryClaimHasAVerdictAndTheCountsReconcile()
    {
        var report = Report();

        Assert.True(report.Claims.Count >= 100, $"Read {report.Claims.Count} claims, expected at least 100.");

        // Out of scope is counted separately and never added to unexamined,
        // so the four have to account for every claim exactly once.
        var counted = report.Count(Verdict.Pass)
            + report.Count(Verdict.Fail)
            + report.Count(Verdict.OutOfScope)
            + report.Count(Verdict.Unexamined);

        Assert.Equal(report.Claims.Count, counted);
    }

    [Fact]
    public void NoClaimPassesByFiat()
    {
        // A PASS names the check that reached it. The floor makes the assertion
        // mean something: over zero passing claims it would hold trivially.
        var passing = Report().Claims.Where(claim => claim.Verdict == Verdict.Pass).ToArray();

        Assert.True(passing.Length >= 4, $"{passing.Length} claims pass, expected at least 4.");
        Assert.DoesNotContain(passing, claim => claim.By.Length == 0);
    }

    [Fact]
    public void NothingIsUnexamined()
    {
        // Green includes that nothing is listed as unexamined. Out of scope is
        // shown beside it and never added to it, because only one of the two is
        // a defect, and every out-of-scope claim names where it ends.
        var report = Report();
        var outOfScope = report.Claims.Where(claim => claim.Verdict == Verdict.OutOfScope).ToArray();

        Assert.Equal(0, report.Count(Verdict.Unexamined));
        Assert.Equal(0, report.Count(Verdict.Fail));
        Assert.True(outOfScope.Length >= 100, $"{outOfScope.Length} claims are out of scope, expected at least 100.");
        Assert.DoesNotContain(outOfScope, claim => !claim.Note.Contains("until", StringComparison.Ordinal));
    }

    [Fact]
    public void SectionFourteenIsReadAsClaimsRatherThanSkipped()
    {
        // The catalogue names section 14 as a claim source and 14 carries an
        // ordered list. A harness that read only tables would have taken no
        // claims from the nightly run and said nothing about having skipped it.
        var steps = NightlyRunSteps.In(File.ReadAllText(Repository.Architecture));

        Assert.True(steps.Count >= 9, $"Read {steps.Count} nightly steps, expected at least 9.");
        Assert.Contains(Report().Claims, claim => claim.Table == NightlyRunSteps.Heading);
    }

    [Fact]
    public void ASectionFourteenWithNoListFailsRatherThanReturningNothing()
    {
        var refusal = Assert.Throws<InvalidOperationException>(
            () => NightlyRunSteps.In("<h2>14. The nightly run, in order</h2><p>none</p><h2>15. The screens</h2>"));

        Assert.Contains("parse failure", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingTableFailsRatherThanReportingZeroClaims()
    {
        // The parse guard, which is the difference between a document that
        // changed and a document with nothing in it.
        var withoutTheCatalogue = ArchitectureTables
            .In(File.ReadAllText(Repository.Architecture))
            .Where(table => table.Heading != "7. Component catalogue")
            .ToArray();

        var refusal = Assert.Throws<InvalidOperationException>(() => PhaseReport.Build(withoutTheCatalogue));

        Assert.Contains("expected and not found", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("7. Component catalogue", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ATableNobodyPlacedStopsTheHarness()
    {
        var withAnExtraTable = ArchitectureTables
            .In(File.ReadAllText(Repository.Architecture))
            .Append(new ArchitectureTable("99. A section nobody placed", [["Header"], ["A row"]]))
            .ToArray();

        var refusal = Assert.Throws<InvalidOperationException>(() => PhaseReport.Build(withAnExtraTable));

        Assert.Contains("nothing would have read them", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADocumentWithNoTablesFailsRatherThanReturningNothing()
    {
        var refusal = Assert.Throws<InvalidOperationException>(
            () => ArchitectureTables.In("<html><body><p>no tables here</p></body></html>"));

        Assert.Contains("Reporting zero claims", refusal.Message, StringComparison.Ordinal);
    }
}
