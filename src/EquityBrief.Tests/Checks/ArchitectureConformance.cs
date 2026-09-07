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
    static PhaseReportModel Report() =>
        PhaseReport.Build(ArchitectureTables.In(File.ReadAllText(Repository.Architecture)));

    [Fact]
    public void EveryTableInTheDocumentIsPlaced()
    {
        var tables = ArchitectureTables.In(File.ReadAllText(Repository.Architecture));

        // Scope, stated in advance, with a floor far enough below the count
        // that ordinary growth never moves it.
        Assert.True(tables.Count >= 20, $"Read {tables.Count} tables, expected at least 20.");

        // Build throws on a table that is neither a claim source nor placed,
        // so reaching this line is the assertion.
        Assert.Equal(tables.Count, Report().Tables.Count);
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
        // A PASS has to name the check that reached it. Vacuous while nothing
        // passes, which is the state 0.5 leaves the report in, and the scope
        // below says so rather than letting a zero read as a result.
        var report = Report();
        var passing = report.Claims.Where(claim => claim.Verdict == Verdict.Pass).ToArray();

        Assert.Equal(0, report.Count(Verdict.Pass));
        Assert.DoesNotContain(passing, claim => claim.By.Length == 0);
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
