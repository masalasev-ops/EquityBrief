using System.Text.Json;
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
    // What this check reaches, declared here rather than in a list beside it.
    // It reads the architecture, which is what lets it be named as covering a
    // whole table; the other three files are what the reconciliation reads to
    // resolve a roster name and a due point.
    internal static CheckReach Reach => new(
        "architecture-conformance",
        ["docs/ARCHITECTURE.html", "CLAUDE.md", "docs/BUILD_PLAN.md", "docs/PROGRESS.md"],
        [
            CheckReach.Key(Scope.CatalogueTable, "Verification harness"),
            CheckReach.Key(Scope.FailureTable, "The harness cannot parse this document"),
            "19.3 What it produces",
        ]);

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
    public void APassNamingNoCheckOnTheRosterIsRefused()
    {
        // The permanent proof for the first half of the fiat guard. Asserting
        // only that By is non-empty is what let a note about the report itself
        // satisfy it.
        var refusal = Assert.Throws<InvalidOperationException>(
            () => Reconcile([new Claim("A table", "A subject", Verdict.Pass, "checked", "no-such-check")]));

        Assert.Contains("is not a check CLAUDE.md's roster carries", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APassNamingACheckThatCannotReachItIsRefused()
    {
        var refusal = Assert.Throws<InvalidOperationException>(
            () => Reconcile([new Claim("A table", "A subject", Verdict.Pass, "checked", "schema-columns")]));

        Assert.Contains("whose declared reach does not include it", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APassRestingOnTheReportItselfIsRefused()
    {
        // The one shape this check exists to prevent and the one it admitted.
        // The claim is otherwise well formed: it names a check on the roster
        // whose declared reach covers it, and it still may not stand.
        var refusal = Assert.Throws<InvalidOperationException>(() => Reconcile(
        [
            new Claim(
                Scope.CatalogueTable,
                "Verification harness",
                Verdict.Pass,
                "this report is the thing the claim describes",
                "architecture-conformance"),
        ]));

        Assert.Contains("offers the report as its own evidence", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APlacementNamingACheckThatDeclaresNoReachIsRefused()
    {
        var refusal = Assert.Throws<InvalidOperationException>(() => Reconcile(
            [],
            [new PlacedTable(Scope.LimitsTable, 0, "constants", Check: "pinned-constants")]));

        Assert.Contains("which declares no reach", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APlacementNamingAnInstrumentThatDoesNotReadTheDocumentIsRefused()
    {
        // Findings 2 and 3 are one defect, and this is it. Section 17 was
        // placed whole against pinned-constants, which covers two constants and
        // never opens the architecture. A check may reach one row without
        // reading the document, because what it asserts is the code; it may not
        // reach a whole table that way, because the rows it would be covering
        // are rows nobody enumerated.
        var refusal = Assert.Throws<InvalidOperationException>(() => Reconcile(
            [],
            [new PlacedTable(Scope.LimitsTable, 0, "constants", Check: "pinned-constants")],
            [new CheckReach("pinned-constants", ["CLAUDE.md", "global.json"], [Scope.LimitsTable])]));

        Assert.Contains("does not read docs/ARCHITECTURE.html", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APlacementOwedAtAPointTheCorpusDoesNotHaveIsRefused()
    {
        var absent = Assert.Throws<InvalidOperationException>(() => Reconcile(
            [],
            [new PlacedTable("A table", 0, "owed later", Due: "9.9")]));

        Assert.Contains("neither as a checkpoint nor as a phase", absent.Message, StringComparison.Ordinal);

        var landed = Assert.Throws<InvalidOperationException>(() => Reconcile(
            [],
            [new PlacedTable("A table", 0, "owed later", Due: "0.6")]));

        Assert.Contains("already records as landed", landed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADeclaredReachNothingSendsAnythingToIsRefused()
    {
        // The other direction. A declaration no placement and no verdict uses
        // is one nothing keeps current, and it would quietly stop describing
        // the check it sits in.
        var refusal = Assert.Throws<InvalidOperationException>(() => Reconcile(
            [],
            [],
            [new CheckReach("schema-columns", ["docs/SCHEMA.md"], ["A table nobody sends"])]));

        Assert.Contains("nothing sends it there", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReportSaysHowManyPlacementsWereReconciled()
    {
        // A reconciliation over zero placements passes silently, so the count
        // is stated on the surface a person reads and carries a floor.
        var report = Report();

        Assert.True(
            report.Reconciled >= Reconciliation.Floor,
            $"{report.Reconciled} placements and verdicts were reconciled, " +
            $"expected at least {Reconciliation.Floor}.");

        Assert.Equal(
            report.Tables.Count(table => table.Check.Length > 0 || table.Due.Length > 0)
                + report.Count(Verdict.Pass),
            report.Reconciled);
    }

    [Fact]
    public void EveryOutOfScopeClaimNamesADuePointThePlanHasAndProgressDoesNot()
    {
        var report = Report();
        var plan = Corpus.Read("docs/BUILD_PLAN.md");
        var progress = Corpus.Read("docs/PROGRESS.md");

        // Read off the note, which is the text a person reads, rather than off
        // the map behind it.
        var due = report.Claims
            .Where(claim => claim.Verdict == Verdict.OutOfScope)
            .Select(claim => claim.Note[(claim.Note.LastIndexOf("until ", StringComparison.Ordinal) + 6)..].Trim())
            .ToArray();

        Assert.True(due.Length >= 100, $"Read {due.Length} out-of-scope claims, expected at least 100.");
        Assert.DoesNotContain(due, point => !DuePoints.InThePlan(point, plan));
        Assert.DoesNotContain(due, point => DuePoints.HasLanded(point, progress));
    }

    [Fact]
    public void TheGeneratedReportNamesTheCheckBehindEveryPass()
    {
        // Over the generated artifact and not over the model. By was populated
        // and asserted non-empty from 0.7 and neither output rendered it, which
        // is what asserting the model rather than the file allows.
        using var elsewhere = new TemporaryDirectory();

        PhaseReportWriter.Write(Report(), elsewhere.Path, DateTimeOffset.UnixEpoch);

        var html = File.ReadAllText(PhaseReportWriter.HtmlPath(elsewhere.Path));
        using var written = JsonDocument.Parse(
            File.ReadAllText(PhaseReportWriter.JsonPath(elsewhere.Path)));

        var passing = written.RootElement.GetProperty("claims").EnumerateArray()
            .Where(claim => claim.GetProperty("verdict").GetString() == "PASS")
            .Select(claim => claim.GetProperty("by").GetString() ?? string.Empty)
            .ToArray();

        Assert.True(passing.Length >= 4, $"{passing.Length} claims pass in the file, expected at least 4.");
        Assert.DoesNotContain(passing, by => by.Length == 0);

        Assert.Contains("<th>Reached by</th>", html, StringComparison.Ordinal);
        Assert.DoesNotContain(passing, by => !html.Contains(by, StringComparison.Ordinal));
    }

    [Fact]
    public void TheVerdictsTheDocumentDefinesAreTheOnesTheReportWrites()
    {
        // 19.3 is placed against this check, so this is what has to reach it.
        var defined = ArchitectureTables
            .In(File.ReadAllText(Repository.Architecture))
            .Single(table => table.Heading == "19.3 What it produces")
            .Body
            .Select(row => row[0])
            .ToArray();

        Assert.Equal(3, defined.Length);

        var written = new[] { Verdict.Pass, Verdict.Fail, Verdict.Unexamined }
            .Select(PhaseReportWriter.Name)
            .ToArray();

        Assert.Equal(written, defined);
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

    // The reconciliation as the harness runs it, over whatever a test hands it.
    // The floor is lifted for these, because each one is proving a refusal
    // rather than measuring a population.
    static int Reconcile(
        IReadOnlyList<Claim> claims,
        IReadOnlyList<PlacedTable>? placed = null,
        IReadOnlyList<CheckReach>? reaches = null) =>
        Reconciliation.Of(
            placed ?? [],
            claims,
            reaches ?? CheckReaches.All(),
            CoverageReported.Roster(),
            Corpus.Read("docs/BUILD_PLAN.md"),
            Corpus.Read("docs/PROGRESS.md"),
            floor: 0);
}
