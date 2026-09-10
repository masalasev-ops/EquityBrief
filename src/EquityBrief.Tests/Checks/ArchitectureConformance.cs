using System.Reflection;
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

            // The system diagram, whole. Its placement says its boxes are the
            // components and stores sections 7 and 16 claim name for name, and
            // this check is what holds that reason to being true.
            "Figure 5.1",
        ]);

    static PhaseReportModel Report()
    {
        var document = File.ReadAllText(Repository.Architecture);

        // Every carried check passing, because what these tests assert is the
        // report's attribution and arithmetic rather than the suite's
        // execution. The execution half has its own proofs below, which write a
        // report from a constructed run and read the written file back.
        return PhaseReport.Build(
            ArchitectureTables.In(document),
            ArchitectureFigures.In(document),
            NightlyRunSteps.In(document),
            Fixtures.Of(Repository.Root),
            CoverageReported.Coverage(),
            SuiteOutcomes.EveryCarriedCheckPassed());
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
    public void EveryFigureInTheDocumentIsPlaced()
    {
        // The other half of the check above, and the half that was missing.
        //
        // `EveryTableInTheDocumentIsPlaced` asserted that every table the reader
        // returned was placed, and the reader matches table elements. Every
        // figure in this document is a div, so four figures and fifty-nine boxes
        // were unread and the completeness check could not say so: its
        // population was defined by the thing it was checking. The phase 4
        // sign-off found one consequence, figure 10.1's rows reached by nothing
        // while the trailing stop rule the code ran drifted from the corpus for
        // a phase.
        var figures = ArchitectureFigures.In(File.ReadAllText(Repository.Architecture));

        // Scope, stated in advance, with a floor far enough below the count that
        // ordinary growth never moves it. The boxes carry the property and the
        // figures are the context, so the floor sits on the boxes.
        var boxes = figures.Sum(figure => figure.Boxes.Count);

        Assert.True(figures.Count >= 4, $"Read {figures.Count} figures, expected at least 4.");
        Assert.True(boxes >= 50, $"Read {boxes} figure boxes, expected at least 50.");

        var placed = Report().Tables.Select(entry => entry.Heading).ToArray();

        Assert.DoesNotContain(figures, figure => !placed.Contains(figure.Id, StringComparer.Ordinal));
    }

    [Fact]
    public void EveryBoxInTheSystemDiagramNamesAComponentOrAStoreTheTablesCarry()
    {
        // Figure 5.1's placement, asserted rather than asserted-by-reason.
        //
        // It is placed as making no claims because its boxes are the components
        // and stores sections 7 and 16 already claim name for name, and a box
        // claimed twice is a claim counted twice. That reason is only true while
        // it holds, so it is checked: a component drawn in the diagram and
        // absent from the catalogue is a component nothing claims, which is the
        // hole the placement would otherwise open.
        var document = File.ReadAllText(Repository.Architecture);
        var figures = ArchitectureFigures.In(document);
        var diagram = figures.Single(figure => figure.Id == "Figure 5.1");

        var tables = ArchitectureTables.In(document);

        string[] Subjects(string heading) =>
        [
            .. tables
                .Where(table => table.Heading == heading)
                .SelectMany(table => table.Body)
                .Where(row => row.Count > 1 && row[0].Length > 0)
                .Select(row => row[0]),
        ];

        var named = Subjects(Scope.CatalogueTable)
            .Concat(Subjects(Scope.StoresTable))
            .ToHashSet(StringComparer.Ordinal);

        // The outside sources are the feeds and archives the system reads, and
        // they are boxes in the diagram rather than rows in either table, which
        // is what "outside the system" means in the band they sit in.
        var inside = diagram.Boxes.Where(box => box.Kind != "src").ToArray();

        Assert.True(inside.Length >= 25, $"Read {inside.Length} boxes inside the system, expected at least 25.");
        Assert.True(named.Count >= 30, $"Read {named.Count} components and stores, expected at least 30.");

        // One box is an aggregate the diagram draws for legibility rather than a
        // store of its own: "Nightly store" groups the five the night writes and
        // names them in its own text, and each of those has a row in section 16.
        // Exempted by name with the reason, and the exemption is asserted to be
        // doing work, because a filter that matches nothing reads as a rule and
        // behaves as a comment.
        const string Aggregate = "Nightly store";

        Assert.Contains(inside, box => box.Name == Aggregate);
        Assert.DoesNotContain(named, name => name == Aggregate);

        var unnamed = inside
            .Where(box => box.Name != Aggregate && !named.Contains(box.Name))
            .Select(box => box.Name)
            .ToArray();

        Assert.DoesNotContain(unnamed, _ => true);

        // And the aggregate is held to naming real stores rather than to being
        // exempt: every store it lists appears in section 16, matched on the
        // row's own words so the diagram cannot name a store the table lost.
        var storeRows = string.Join(" ", Subjects(Scope.StoresTable)).ToLowerInvariant();

        var grouped = inside
            .Single(box => box.Name == Aggregate)
            .Rule
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Assert.True(grouped.Length >= 5, $"The aggregate names {grouped.Length} stores, expected at least 5.");

        Assert.All(
            grouped,
            store => Assert.True(
                storeRows.Contains(store.ToLowerInvariant(), StringComparison.Ordinal)
                || storeRows.Contains(store.ToLowerInvariant().TrimEnd('s'), StringComparison.Ordinal),
                $"the nightly store box names '{store}' and section 16 has no row carrying it."));

        // The outside boxes are counted rather than passed over, so a source
        // added to the diagram is visible in a figure a person reads.
        Assert.True(
            diagram.Boxes.Count - inside.Length >= 7,
            $"Read {diagram.Boxes.Count - inside.Length} outside sources, expected at least 7.");
    }

    [Fact]
    public void EveryClaimHasAVerdictAndTheCountsReconcile()
    {
        var report = Report();

        Assert.True(report.Claims.Count >= 80, $"Read {report.Claims.Count} claims, expected at least 100.");

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

        Assert.True(passing.Length >= 22, $"{passing.Length} claims pass, expected at least 22. 29 do at 1.4, and this only grows.");
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

        // The floor falls as the build advances, which is what it is for: it
        // stops this half passing over an empty set. Lowered from 100 at 5.4,
        // where seventeen claims became PASS at once, and it will fall again.
        // The claims themselves are what carries the property; this number is a
        // fact about how far the build has got.
        Assert.True(due.Length >= 80, $"Read {due.Length} out-of-scope claims, expected at least 80.");
        Assert.DoesNotContain(due, point => !DuePoints.InThePlan(point, plan));
        Assert.DoesNotContain(due, point => DuePoints.HasLanded(point, progress));
    }

    [Fact]
    public void APlanningPassDoesNotLandItsPhaseAndACheckpointDoes()
    {
        // The permanent proof, over a constructed record rather than the real
        // one, because the property has to hold for a phase this repository has
        // not reached and cannot be shown on a file that has reached none.
        //
        // Both directions, and the second is the one that matters most. A
        // tightening that made this answer no forever would be green today and
        // wrong at every sign-off from here on, which is the shape of a check
        // that narrows its own scope and keeps passing.
        const string planningOnly = """
            ### 3.0 planning - the pass that settles what phase 3 builds against
            Not a checkpoint entry. It belongs to 3.0, which has not landed.

            ### 1.2 - the one-year backfill
            Built:      the backfill.
            """;

        Assert.False(DuePoints.HasLanded("phase 3", planningOnly));
        Assert.False(DuePoints.HasLanded("3.0", planningOnly));
        Assert.True(DuePoints.HasLanded("phase 1", planningOnly));

        const string built = """
            ### 3.1 - the indicator engine and the averages on the chart
            Built:      the indicator engine.
            """;

        Assert.True(DuePoints.HasLanded("phase 3", built));
        Assert.True(DuePoints.HasLanded("3.1", built));

        // The number is not what tells them apart. An entry headed with a
        // building checkpoint whose body opens the planning way is a planning
        // pass, and the old matcher had no way to see that at all.
        const string numberedLikeACheckpoint = """
            ### 3.1 - the pass that settles what phase 3 builds against
            Not a checkpoint entry. It belongs to 3.1, which has not landed.
            """;

        Assert.False(DuePoints.HasLanded("phase 3", numberedLikeACheckpoint));
        Assert.False(DuePoints.HasLanded("3.1", numberedLikeACheckpoint));
    }

    [Fact]
    public void EveryNightlyStepIsAnsweredByExactlyOneKey()
    {
        // The first instance of the prefix class, and the one that had no
        // guard. Section 14's steps are sentences and the keys are their
        // openings, matched with StartsWith, so a key that is the opening of
        // another answers for both and the dictionary's first match wins with
        // nothing reporting the collision.
        //
        // Both directions. Zero matches leaves a step with no due point, which
        // the caller already refuses; two leaves one answered by the wrong
        // entry, which nothing saw.
        var steps = NightlyRunSteps.In(File.ReadAllText(Repository.Architecture));

        Assert.True(steps.Count >= 8, $"Read {steps.Count} nightly steps, expected at least 8.");

        var ambiguous = steps.Where(step => Scope.NightlyStepKeysMatching(step) != 1).ToArray();

        Assert.True(
            ambiguous.Length == 0,
            "These nightly steps are matched by other than exactly one key: " +
            string.Join("; ", ambiguous.Select(step => $"{step} ({Scope.NightlyStepKeysMatching(step)})")) + ".");

        var unused = Scope.NightlyStepKeys()
            .Where(key => !steps.Any(step => step.StartsWith(key, StringComparison.Ordinal)))
            .ToArray();

        Assert.True(
            unused.Length == 0,
            "These nightly step keys match no step in the document: " + string.Join("; ", unused) + ".");
    }

    [Fact]
    public void TheRecordsOwnEntriesAreReadAsBuiltOrAsPlanning()
    {
        // Over the real record, so the reader is exercised against the shapes
        // the file actually carries rather than only against constructed ones.
        // A parse returning nothing would pass every assertion above.
        var built = DuePoints.Built(Corpus.Read("docs/PROGRESS.md"));

        Assert.True(built.Count >= 8, $"Read {built.Count} built checkpoints from PROGRESS, expected at least 8.");

        Assert.Contains("1.1", built);
        Assert.Contains("1.2", built);

        // The other direction, against a checkpoint far enough out that this
        // does not have to be edited as the build advances. The first version
        // of it named 1.3, which was true when it was written and false an hour
        // later when 1.3's entry landed: a negative direction keyed on the
        // checkpoint in hand is one that has to be rewritten to stay true, and
        // one rewritten that often stops being read.
        Assert.DoesNotContain("6.8", built);
        Assert.DoesNotContain("9.9", built);
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

        Assert.True(passing.Length >= 28, $"{passing.Length} claims pass in the file, expected at least 28.");
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
        Assert.True(outOfScope.Length >= 80, $"{outOfScope.Length} claims are out of scope, expected at least 80.");
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

    // A due point written into Scope is a second statement of which checkpoint
    // does the work, and BUILD_PLAN's checkpoint text is the first. These three
    // keep the two from drifting, which is what nobody had when 8ac2442
    // reordered phase 1 and left fifteen due points describing the old order.

    [Fact]
    public void EveryDuePointThePlanSuppliesIsReadFromThePlan()
    {
        var checkpoints = PlanCheckpoints.All();
        var exceptions = Scope.DeclaredExceptions();

        var shadowing = Scope.ResidualSubjects()
            .Where(subject => !exceptions.Contains(subject, StringComparer.Ordinal))
            .Where(subject => PlanCheckpoints.DueFor(subject, checkpoints) is not null)
            .Select(subject => $"{subject} (plan says {PlanCheckpoints.DueFor(subject, checkpoints)})")
            .ToArray();

        Assert.True(
            shadowing.Length == 0,
            "These subjects are written into Scope and BUILD_PLAN names them, so the written " +
            "value shadows the plan and will not move when the plan is reordered: " +
            string.Join("; ", shadowing) +
            ". Delete the entry and let it derive, or declare it an exception with the reason.");
    }

    [Fact]
    public void EveryDeclaredExceptionRunsInTheDirectionItClaims()
    {
        var checkpoints = PlanCheckpoints.All();
        var exceptions = Scope.Exceptions();

        Assert.True(exceptions.Count >= 2, $"Read {exceptions.Count} declared exceptions, expected at least 2.");

        var wrong = new List<string>();

        foreach (var exception in exceptions)
        {
            var derived = PlanCheckpoints.DueFor(exception.Subject, checkpoints);

            if (derived is null)
            {
                wrong.Add(
                    $"{exception.Subject} is declared an exception and the plan does not name it, " +
                    "so there is no derivation to except it from.");

                continue;
            }

            var order = DuePoints.Compare(derived, exception.Declared);

            // The label is not taken on trust. A mislabelled exception is the
            // one failure the split into two lists cannot otherwise catch: an
            // unsafe derivation filed under the safe heading passes every other
            // assertion here and then fails the day its checkpoint lands.
            if (exception.Later && order <= 0)
            {
                wrong.Add(
                    $"{exception.Subject} is declared as derived later than the truth, and the plan " +
                    $"derives {derived} against a declared {exception.Declared}, which is not later. " +
                    "A late exception can only delay a claim; an early one fails the day its " +
                    "checkpoint lands, so this belongs in the other list.");
            }

            if (!exception.Later && order >= 0)
            {
                wrong.Add(
                    $"{exception.Subject} is declared as derived earlier than the truth, and the plan " +
                    $"derives {derived} against a declared {exception.Declared}, which is not earlier. " +
                    "An exception declared unsafe and reported every run should be the safe kind.");
            }
        }

        Assert.Empty(wrong);
    }

    [Fact]
    public void TheUnsafeExceptionsAreOnTheSurfaceAPersonReads()
    {
        // The lesson of finding 1 of the phase 0 review, applied before it can
        // repeat: a claim that something is visible is a claim about a surface,
        // so this reads the written files rather than the model behind them.
        // Generated into a temporary directory rather than read out of
        // artifacts/, which is gitignored and only exists once verify-phase has
        // been run. verify-phase is deliberately not a CI step, so the first
        // version of this test passed on this machine from a leftover file and
        // failed on all three runners. A test that depends on another command
        // having been run is a test that reports the state of a working copy.
        using var elsewhere = new TemporaryDirectory();

        PhaseReportWriter.Write(Report(), elsewhere.Path, DateTimeOffset.UnixEpoch);

        var json = File.ReadAllText(PhaseReportWriter.JsonPath(elsewhere.Path));
        var html = File.ReadAllText(PhaseReportWriter.HtmlPath(elsewhere.Path));

        var unsafeOnes = Scope.Exceptions().Where(exception => !exception.Later).ToArray();

        Assert.NotEmpty(unsafeOnes);

        // The section itself, not the whole page. A subject named here is also a
        // claim subject elsewhere on the page, so searching the whole document
        // would pass on the claims table and prove nothing about this section.
        var start = html.IndexOf("<h2>Unsafe due-point exceptions", StringComparison.Ordinal);

        Assert.True(start >= 0, "The phase report carries no unsafe due-point exceptions section.");

        var next = html.IndexOf("<h2>", start + 4, StringComparison.Ordinal);
        var section = next < 0 ? html[start..] : html[start..next];

        foreach (var exception in unsafeOnes)
        {
            Assert.Contains(exception.Subject, json, StringComparison.Ordinal);
            Assert.Contains($">{exception.Subject}<", section, StringComparison.Ordinal);
            Assert.Contains($">{exception.Declared}<", section, StringComparison.Ordinal);
        }

        // And the safe ones stay out of that section, or it stops meaning what
        // it says and becomes a list of every exception rather than the ones
        // worth looking at again.
        foreach (var quiet in Scope.Exceptions().Where(exception => exception.Later))
        {
            Assert.DoesNotContain($">{quiet.Subject}<", section, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheOrderingPutsAPhaseBeforeItsCheckpoints()
    {
        // The permanent proof under the direction assertion above, since a
        // comparator that returned zero for everything would pass every case.
        Assert.True(DuePoints.Compare("1.3", "1.4") < 0);
        Assert.True(DuePoints.Compare("6.1", "4.5") > 0);
        Assert.True(DuePoints.Compare("1.7", "5.1") < 0);
        Assert.Equal(0, DuePoints.Compare("2.4", "2.4"));
        Assert.True(DuePoints.Compare("phase 5", "5.1") < 0);
        Assert.True(DuePoints.Compare("phase 5", "4.7") > 0);
    }

    [Fact]
    public void EveryOutOfScopeClaimHasExactlyOneOrigin()
    {
        var tables = ArchitectureTables.In(File.ReadAllText(Repository.Architecture));
        var report = PhaseReport.Build(
            tables,
            ArchitectureFigures.In(File.ReadAllText(Repository.Architecture)),
            NightlyRunSteps.In(File.ReadAllText(Repository.Architecture)),
            outcomes: SuiteOutcomes.EveryCarriedCheckPassed());

        var outOfScope = report.Claims.Where(claim => claim.Verdict == Verdict.OutOfScope).ToArray();

        var byOrigin = outOfScope
            .GroupBy(claim => Scope.Resolve(claim.Table, claim.Subject).Origin)
            .ToDictionary(group => group.Key, group => group.Count());

        int Count(DueOrigin origin) => byOrigin.TryGetValue(origin, out var found) ? found : 0;

        var plan = Count(DueOrigin.Plan);
        var screens = Count(DueOrigin.Screens);
        var written = Count(DueOrigin.Residual);
        var excepted = Count(DueOrigin.Exception);

        // The property, and the reason this replaced a single count. Every claim
        // out of scope was answered by exactly one of the four, so a claim
        // answered by none cannot be counted as answered by all of them, and a
        // sum that does not reach the total means a branch has appeared that
        // nothing here is measuring.
        Assert.Equal(0, Count(DueOrigin.Nothing));
        Assert.Equal(outOfScope.Length, plan + screens + written + excepted);

        // Three scopes, and only one of them is floored.
        //
        // The claims out of scope is a fact about how much of the system is
        // unbuilt. So is the count answered by the plan: every checkpoint that
        // lands moves claims out of this population, and at phase 7 it is zero
        // by construction. Neither size is a fact about the property, so the
        // floor sits far enough below the value that ordinary building never
        // reaches it, and catches the one thing worth catching: a derivation
        // that has stopped resolving anything at all.
        //
        // The floor that stood here was 40 against a count of 45, and it was
        // anchored against a population this check no longer has. Fifteen of
        // those 45 were section 15 rows answered by their table heading, which
        // the old count read as derived from the plan because the plan's prose
        // contains their words. Measured by origin the same tree gives 30, so
        // the old floor did not survive the correction and could not be carried.
        // Lowered from 20 to 12 at 5.5. The number falls as the build advances
        // and each checkpoint turns a batch of plan-derived due points into
        // verdicts, so this floor is a fact about how far the build has got
        // rather than about the property. What carries the property is the split
        // by origin above, which cannot be satisfied by an empty set.
        Assert.True(
            plan >= 12,
            $"{plan} out-of-scope claims take their due point from BUILD_PLAN, expected at least 12. " +
            $"59 did when this floor was first set, over {outOfScope.Length} claims out of scope, " +
            $"beside {screens} from section 15, {written} written into Scope and {excepted} declared exceptions.");
    }

    [Fact]
    public void EveryScreenRowHasItsOwnDuePointAndNoneIsWrittenForARowThatIsGone()
    {
        // Contradiction D, in both directions. Keyed on the table heading alone,
        // a section's rows shared one due point and a row could be added to the
        // document without anybody deciding when it is owed. Keyed on the row,
        // omission has to be caught, because a written map is only as current as
        // the thing it is read against.
        var tables = ArchitectureTables.In(File.ReadAllText(Repository.Architecture));

        var screensTables = tables
            .Where(table => Scope.ScreensTables.Contains(table.Heading, StringComparer.Ordinal))
            .ToArray();

        var inDocument = screensTables
            .SelectMany(table => table.Body
                .Where(row => row.Count > 0)
                .SelectMany(row => Scope.SubjectsOf(table.Heading, row[0])
                    .Select(subject => CheckReach.Key(table.Heading, subject))))
            .ToArray();

        Assert.Equal(Scope.ScreensTables.Length, screensTables.Length);

        // Stated in advance: seven tables, 37 rows, 44 claim subjects. The
        // Level chart row decomposes into its four elements, and 5.0 decomposed
        // three more per surface, being the universe screen's sector strip and
        // table, whose listing halves cannot exist until 5.4 creates that
        // store, and the run page's reason record, whose counts are 5.6's and
        // whose verdicts need resolved setups. A run finding none would
        // otherwise pass both directions over an empty set.
        Assert.Equal(37, screensTables.Sum(table => table.Body.Count(row => row.Count > 0)));
        Assert.Equal(44, inDocument.Length);

        var written = Scope.ScreensKeys();

        var undecided = inDocument.Where(key => !written.Contains(key, StringComparer.Ordinal)).ToArray();
        var stale = written.Where(key => !inDocument.Contains(key, StringComparer.Ordinal)).ToArray();

        Assert.True(
            undecided.Length == 0,
            "These section 15 rows have no due point of their own: " + string.Join("; ", undecided) +
            ". A row added to the document without one would inherit nothing, and inheriting a " +
            "table's point is what contradiction D was.");

        Assert.True(
            stale.Length == 0,
            "These due points name a section 15 row the document no longer has: " +
            string.Join("; ", stale) + ".");
    }

    [Fact]
    public void EveryDecomposedElementIsNamedByTheRowItDecomposes()
    {
        // Contradiction F. A row read as four claims is a decomposition the
        // document does not carry, so the one thing that keeps it from being a
        // second statement of the row's content is that each element is read
        // back out of the row's own description. Rename an element in the
        // document, or invent one here, and this fails.
        var tables = ArchitectureTables.In(File.ReadAllText(Repository.Architecture));
        var rows = Scope.DecomposedRows();

        Assert.NotEmpty(rows);

        var unnamed = ElementsNotInTheirRow(tables, rows.ToDictionary(
            key => key, Scope.ElementsOf, StringComparer.Ordinal), out var checkedElements);

        Assert.True(
            unnamed.Count == 0,
            "These elements are read as claims and the row they decompose does not name them: " +
            string.Join("; ", unnamed) +
            ". A decomposition the document does not carry is a second statement of the row's content.");

        // Stated in advance, and it is the scope carrying the property: twenty
        // elements over eight rows, four on the level chart, two on the gap
        // failure, two on the unavailable feed, two on the two-hundred-bar row
        // 3.1 decomposed, and six 5.0 added. Zero would pass every assertion
        // above.
        //
        // An exact count rather than a floor, so a decomposition added without
        // being argued for fails here. It moved from eight at 3.1, where the
        // two-hundred-bar row was split because its behaviour half is the stored
        // indicator and its other half is a string on a page nothing draws yet,
        // and from twelve at 5.0, where the universe screen's sector strip and
        // table were split at the listing store 5.1 does not have and the run
        // page's reason record was split between the counts 5.6 draws and the
        // verdicts that need resolved setups.
        Assert.Equal(20, checkedElements);
    }

    [Fact]
    public void TheCheckReportsAnElementTheRowDoesNotName()
    {
        // The permanent proof, over a constructed table rather than the real
        // one. Three elements are named in the description and one is not, and
        // the one that is not is the only one reported.
        var tables = ArchitectureTables.In("""
            <html><body><h3>15.5 The mark vocabulary</h3><table>
            <tr><th>Mark</th><th>What it is</th></tr>
            <tr><td>Level chart</td><td>Daily candles with the level bands shaded behind them and a volume pane beneath.</td></tr>
            </table></body></html>
            """);

        var declared = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [CheckReach.Key("15.5 The mark vocabulary", "Level chart")] =
                ["candles", "the level bands", "the moving averages", "a volume pane"],
        };

        var unnamed = ElementsNotInTheirRow(tables, declared, out var checkedElements);

        Assert.Equal(4, checkedElements);
        Assert.Contains("the moving averages", Assert.Single(unnamed), StringComparison.Ordinal);
    }

    // Shared by the check and its proof, so the proof exercises the same reader
    // rather than a copy of it that can drift.
    static IReadOnlyList<string> ElementsNotInTheirRow(
        IReadOnlyList<ArchitectureTable> tables,
        IReadOnlyDictionary<string, IReadOnlyList<string>> declared,
        out int checkedElements)
    {
        var unnamed = new List<string>();
        checkedElements = 0;

        foreach (var (key, elements) in declared)
        {
            var parts = key.Split(CheckReach.Joiner);
            var table = Assert.Single(tables, candidate => candidate.Heading == parts[0]);

            // This row, not the whole table. A phrase found in a neighbouring
            // row would prove nothing about this one.
            //
            // Every cell of it rather than the description alone, because the
            // two tables that decompose put their elements in different
            // columns: the mark vocabulary names them where it says what the
            // mark is, and the failure table names them in "What you see".
            // Reading one index would have been a rule about column order.
            var row = Assert.Single(
                table.Body, candidate => candidate.Count > 1 && candidate[0] == parts[1]);

            var text = string.Join(" | ", row);

            foreach (var element in elements)
            {
                checkedElements++;

                if (!text.Contains(element, StringComparison.OrdinalIgnoreCase))
                {
                    unnamed.Add($"{key}, {element}");
                }
            }
        }

        return unnamed;
    }

    [Fact]
    public void TheCheckReportsADuePointThatDidNotMoveWithThePlan()
    {
        // The permanent proof, over a constructed plan rather than the real one.
        // A subject named at 1.3 derives 1.3; move the text to 1.4 and the
        // derived value follows it, which is the whole property. A written due
        // point would still say 1.3 and nothing would notice.
        const string before = """
            ### 1.3 The read surface and the chart
            The read API serving bars, and the bar fetcher.

            ### 1.4 The bar fetcher and the nightly script
            One bulk request per night.
            """;

        const string after = """
            ### 1.3 The read surface and the chart
            The read API serving bars.

            ### 1.4 The bar fetcher and the nightly script
            One bulk request per night.
            """;

        Assert.Equal("1.3", PlanCheckpoints.DueFor("Bar fetcher", PlanCheckpoints.In(before, floor: 2)));
        Assert.Equal("1.4", PlanCheckpoints.DueFor("Bar fetcher", PlanCheckpoints.In(after, floor: 2)));

        // A planning checkpoint names what it settles and builds none of it, so
        // it can never be the answer. 4.0 named four components this way and
        // reading a due point from it put every one of them before its code.
        const string planning = """
            ### 4.0 Planning
            Settles the trend classifier's rule.

            ### 4.1 The calendar fetcher and the trend state
            The trend classifier to the rule settled at 4.0.
            """;

        Assert.Equal("4.1", PlanCheckpoints.DueFor("Trend classifier", PlanCheckpoints.In(planning, floor: 2)));

        // And a subject the plan does not name derives nothing, which is what
        // sends it to the residual map rather than to a wrong answer.
        Assert.Null(PlanCheckpoints.DueFor("Report exporter", PlanCheckpoints.In(before, floor: 2)));
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

    // A report built from a run in which one check failed. This is the whole
    // point of the 0.7 repair, so it is asserted over the written file rather
    // than over the model: the surface was the defect the last time this class
    // of thing appeared, when By was populated and asserted non-empty from 0.7
    // and neither output rendered it.
    static PhaseReportModel ReportFrom(SuiteOutcomes outcomes)
    {
        var document = File.ReadAllText(Repository.Architecture);

        return PhaseReport.Build(
            ArchitectureTables.In(document),
            ArchitectureFigures.In(document),
            NightlyRunSteps.In(document),
            Fixtures.Of(Repository.Root),
            CoverageReported.Coverage(),
            outcomes);
    }

    static Dictionary<string, CheckResult> EveryCarriedCheck(CheckRun run) =>
        CoverageReported.Coverage()
            .Where(check => check.Carrier != CoverageReported.NotDueYet)
            .ToDictionary(check => check.Check, _ => new CheckResult(run), StringComparer.Ordinal);

    [Fact]
    public void AFailedCheckMakesItsClaimsFailOnTheWrittenReport()
    {
        // The check to fail is taken from the report itself rather than named
        // here, so this cannot go quietly green by naming a check that has
        // stopped reaching anything.
        var passing = ReportFrom(SuiteOutcomes.EveryCarriedCheckPassed())
            .Claims
            .Where(claim => claim.Verdict == Verdict.Pass)
            .ToArray();

        Assert.NotEmpty(passing);

        var failing = passing[0].By;
        var expected = passing.Count(claim => claim.By == failing);

        Assert.True(expected >= 1, $"'{failing}' reaches {expected} passing claims, expected at least 1.");

        var outcomes = EveryCarriedCheck(CheckRun.Passed);

        outcomes[failing] = new CheckResult(
            CheckRun.Failed,
            failing + ".SomeTest",
            "Assert.Equal() Failure: Values differ");

        using var elsewhere = new TemporaryDirectory();

        PhaseReportWriter.Write(
            ReportFrom(SuiteOutcomes.Of(outcomes)), elsewhere.Path, DateTimeOffset.UnixEpoch);

        using var written = JsonDocument.Parse(
            File.ReadAllText(PhaseReportWriter.JsonPath(elsewhere.Path)));

        var summary = written.RootElement.GetProperty("summary");

        // fail reads the number of claims that check reached, and never zero.
        // Every "fail 0" the harness printed from 0.5 through 1.8 was
        // structural: Verdict.Fail was assigned nowhere in the report path, so
        // the line could not take another value.
        Assert.Equal(expected, summary.GetProperty("fail").GetInt32());
        Assert.False(written.RootElement.GetProperty("green").GetBoolean());
        Assert.Equal(passing.Length - expected, summary.GetProperty("pass").GetInt32());

        // And on the page, with what the check found beside the claim, because
        // 19.3 says a failure shows the diff beside it.
        var html = File.ReadAllText(PhaseReportWriter.HtmlPath(elsewhere.Path));

        Assert.Contains("FAIL", html, StringComparison.Ordinal);
        Assert.Contains("Values differ", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ACheckThatDidNotRunLeavesItsClaimsUnexaminedAndNeverPassing()
    {
        // 19.3: a claim that was not checked is UNEXAMINED and this never counts
        // as a pass. The report reads a result rather than a declaration, so a
        // run that produced no result passes nothing at all.
        using var elsewhere = new TemporaryDirectory();

        PhaseReportWriter.Write(
            ReportFrom(SuiteOutcomes.NothingRan), elsewhere.Path, DateTimeOffset.UnixEpoch);

        using var written = JsonDocument.Parse(
            File.ReadAllText(PhaseReportWriter.JsonPath(elsewhere.Path)));

        var summary = written.RootElement.GetProperty("summary");

        Assert.Equal(0, summary.GetProperty("pass").GetInt32());
        Assert.Equal(0, summary.GetProperty("fail").GetInt32());
        Assert.True(summary.GetProperty("unexamined").GetInt32() > 0);
        Assert.False(written.RootElement.GetProperty("green").GetBoolean());

        // Out of scope is unmoved by any of this. It is a statement about the
        // plan rather than about a run, and adding it to unexamined is the
        // conflation the rules forbid.
        Assert.Equal(
            ReportFrom(SuiteOutcomes.EveryCarriedCheckPassed()).Count(Verdict.OutOfScope),
            summary.GetProperty("outOfScope").GetInt32());
    }

    [Fact]
    public void TheSuiteResultIsReadFromTheFileTheRunWrites()
    {
        // The trx reader, over a constructed file. Three outcomes and the
        // verdicts they produce, so the mapping is asserted rather than assumed
        // from the one shape a passing run happens to write.
        using var elsewhere = new TemporaryDirectory();

        var trx = Path.Combine(elsewhere.Path, "suite.trx");

        // Passing is the only outcome that is a pass. Each of the others is
        // written beside a passing sibling of the same carrier, which is the
        // case that let the first version of this reader call a skipped or
        // errored check passed: two Skip attributes were the whole distance
        // between this repair working and not working.
        foreach (var outcome in new[] { "Failed", "Error", "Timeout", "Aborted" })
        {
            File.WriteAllText(trx, Constructed("ComponentAccess", outcome));

            Assert.Equal(CheckRun.Failed, SuiteOutcomes.FromTrx(trx).For("component-access").Run);
        }

        File.WriteAllText(trx, Constructed("ComponentAccess", "NotExecuted"));

        var skipped = SuiteOutcomes.FromTrx(trx);

        // Skipped is not run rather than run and failed, and it is never a pass.
        Assert.Equal(CheckRun.DidNotRun, skipped.For("component-access").Run);
        Assert.Equal(CheckRun.Passed, skipped.For("schema-columns").Run);
        Assert.Contains(
            "did not match",
            SuiteOutcomes.FromTrx(WrittenWith(trx, "ComponentAccess", "Failed")).For("component-access").Message,
            StringComparison.Ordinal);

        // A check with no test in the file did not run, and a file that is not
        // there is a run that said nothing. Neither of those is a pass.
        Assert.Equal(CheckRun.DidNotRun, skipped.For("nightly-run").Run);

        var absent = SuiteOutcomes.FromTrx(Path.Combine(elsewhere.Path, "absent.trx"));

        Assert.Equal(CheckRun.DidNotRun, absent.For("schema-columns").Run);
        Assert.False(absent.Run.Clean);
    }

    static string WrittenWith(string path, string carrier, string outcome)
    {
        File.WriteAllText(path, Constructed(carrier, outcome));

        return path;
    }

    [Fact]
    public void ARunWithAFailureOutsideEveryCarrierStillRedensTheReport()
    {
        // The second population, and the reason the report reads the run as
        // well as the carried checks. 81 of the suite's tests sit in classes
        // carrying no check, so a failure in one of them moves no claim. Before
        // this the report printed green and exited 0 over a red suite, which is
        // the fault this repair exists to remove, one level out.
        var outcomes = SuiteOutcomes.Of(
            EveryCarriedCheck(CheckRun.Passed),
            new SuiteRun(Total: 256, Executed: 256, Failed: 1, NotExecuted: 0));

        var report = ReportFrom(outcomes);

        // No claim moved, and the report is still not green.
        Assert.Equal(0, report.Count(Verdict.Fail));
        Assert.Equal(0, report.Count(Verdict.Unexamined));
        Assert.False(report.Green);

        using var elsewhere = new TemporaryDirectory();

        PhaseReportWriter.Write(report, elsewhere.Path, DateTimeOffset.UnixEpoch);

        using var written = JsonDocument.Parse(
            File.ReadAllText(PhaseReportWriter.JsonPath(elsewhere.Path)));

        Assert.False(written.RootElement.GetProperty("green").GetBoolean());

        // And the run is on the artifact rather than only in the scrollback,
        // because a claim that something is visible is a claim about a surface.
        var suite = written.RootElement.GetProperty("suite");

        Assert.Equal(1, suite.GetProperty("failed").GetInt32());
        Assert.False(suite.GetProperty("clean").GetBoolean());
        Assert.Contains(
            "1 failed",
            File.ReadAllText(PhaseReportWriter.HtmlPath(elsewhere.Path)),
            StringComparison.Ordinal);

        // A skipped test is the same fault by a different name.
        Assert.False(ReportFrom(SuiteOutcomes.Of(
            EveryCarriedCheck(CheckRun.Passed),
            new SuiteRun(256, 254, 0, 2))).Green);

        // And a run that executed nothing is not a clean run.
        Assert.False(ReportFrom(SuiteOutcomes.Of(
            EveryCarriedCheck(CheckRun.Passed),
            new SuiteRun(0, 0, 0, 0))).Green);
    }

    // Every outcome the format writes, and each beside a passing sibling of the
    // same carrier, because that is the shape that made the first version of
    // this reader wrong: it asked only whether any row said "Failed" and let a
    // passing sibling answer for the rest.
    static string Constructed(string carrier, string outcome) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
        "<TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\">\n" +
        "  <Results>\n" +
        "    <UnitTestResult testName=\"EquityBrief.Tests.Checks.SchemaColumns.One\" outcome=\"Passed\" />\n" +
        $"    <UnitTestResult testName=\"EquityBrief.Tests.Checks.{carrier}.First\" outcome=\"Passed\" />\n" +
        $"    <UnitTestResult testName=\"EquityBrief.Tests.Checks.{carrier}.Second\" outcome=\"{outcome}\">\n" +
        "      <Output><ErrorInfo><Message>the declaration did not match</Message></ErrorInfo></Output>\n" +
        "    </UnitTestResult>\n" +
        "  </Results>\n" +
        "  <ResultSummary><Counters total=\"3\" executed=\"3\" passed=\"2\" failed=\"1\" " +
        "error=\"0\" timeout=\"0\" aborted=\"0\" notExecuted=\"0\" /></ResultSummary>\n" +
        "</TestRun>\n";

    [Fact]
    public void TheCarrierOfEveryCheckOwnsItsTestsAndNoOthers()
    {
        // The prefix property, in both directions. A carrier is matched on its
        // type's full name and a dot, and this is what says the keys are
        // disjoint: a test belongs to exactly one carrier, and every carrier
        // owns at least one test. Keyed on the class name alone, "Store" would
        // answer for "StoreWrites" and two checks would share one result.
        var carriers = CoverageReported.Coverage()
            .Where(check => check.Carrier != CoverageReported.NotDueYet)
            .Select(check => check.Carrier)
            .Distinct(StringComparer.Ordinal)
            .Select(SuiteOutcomes.CarrierType)
            .ToArray();

        Assert.True(carriers.Length >= 20, $"Resolved {carriers.Length} carriers, expected at least 20.");

        // Over every test in the suite and not only over the carriers' own,
        // which is what the first version did: it built this list by reflecting
        // over the carriers, so both loops below asked only about tests the
        // carriers already declared and the question that matters, whether a
        // test is owned at all, could not be reached.
        var tests = Assembly.GetExecutingAssembly().GetTypes()
            .Where(type => type.IsPublic)
            .SelectMany(type => type.GetMethods()
                .Where(method => method.GetCustomAttributes()
                    .Any(attribute => attribute.GetType().Name is "FactAttribute" or "TheoryAttribute"))
                .Select(method => type.FullName + "." + method.Name))
            .ToArray();

        Assert.True(tests.Length >= 200, $"Found {tests.Length} tests in the suite, expected at least 200.");

        // Tests owned by no carrier are not a defect and are not zero: eleven
        // classes test components rather than carry a check. They are counted
        // rather than assumed, and the report reads the run's own tally so a
        // failure in one of them still reddens it.
        var unowned = tests
            .Count(test => !carriers.Any(type => test.StartsWith(type.FullName + ".", StringComparison.Ordinal)));

        Assert.True(unowned >= 40, $"{unowned} tests are owned by no carrier, expected at least 40.");

        // At most one, which is the prefix property. Zero is allowed and
        // counted above; two would mean one carrier's full name is a prefix of
        // another's and two checks would share a result.
        foreach (var test in tests)
        {
            var owners = carriers.Count(
                type => test.StartsWith(type.FullName + ".", StringComparison.Ordinal));

            Assert.True(
                owners <= 1,
                $"{test} is owned by {owners} carriers, where it has to be owned by at most one.");
        }

        foreach (var type in carriers)
        {
            Assert.Contains(
                tests, test => test.StartsWith(type.FullName + ".", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void EveryCheckAPassingClaimNamesIsOneTheSuiteResultCanAnswerFor()
    {
        // The join between Scope and the suite's result. A passing claim names a
        // check, the result is keyed on the roster's check names, and a name in
        // the first that is missing from the second would read as a check that
        // did not run. That is the safe direction and it would still be wrong.
        var named = ReportFrom(SuiteOutcomes.EveryCarriedCheckPassed())
            .Claims
            .Where(claim => claim.Verdict == Verdict.Pass)
            .Select(claim => claim.By)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(named.Length >= 8, $"{named.Length} distinct checks reach a passing claim, expected at least 8.");

        var answerable = CoverageReported.Coverage()
            .Where(check => check.Carrier != CoverageReported.NotDueYet)
            .Select(check => check.Check)
            .ToArray();

        Assert.DoesNotContain(named, check => !answerable.Contains(check, StringComparer.Ordinal));
    }
}
