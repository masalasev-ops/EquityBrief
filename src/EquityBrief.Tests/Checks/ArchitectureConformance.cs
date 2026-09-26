using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Checks;

// architecture-conformance. Every claim a table in ARCHITECTURE.html makes has a
// verdict, and every table in the document is placed so none can go unread.
//
// This runs every CI run and asserts the shape of the report. Whether the
// verdicts are good enough to sign a phase off is tools/verify-phase's question,
// and it is a gate on a phase rather than on a commit.
public partial class ArchitectureConformance
{
    // What this check reaches, declared here rather than in a list beside it.
    // It reads the architecture, which is what lets it be named as covering a
    // whole table; the other three files are what the reconciliation reads to
    // resolve a roster name and a due point. The roster moved to
    // `.claude/rules/checks.md`, so that is the file named here.
    internal static CheckReach Reach => new(
        "architecture-conformance",
        [".claude/rules/checks.md", "docs/ARCHITECTURE.html", "docs/BUILD_PLAN.md", "docs/PROGRESS.md"],
        [
            CheckReach.Key(Scope.CatalogueTable, "Verification harness"),
            CheckReach.Key(Scope.FailureTable, "The harness cannot parse this document"),
            "19.3 What it produces",

            // 8.7, the phase 8 report. The loop's own two tables, whole: 13.2's
            // rows say which checkpoint builds each thing that can improve, and
            // 13.3's guardrails are the rules the loop is safe under. Both are
            // reconciliations between the document and this harness, which is
            // what this check is, and neither is assertable until the loop it
            // describes is built.
            "13.2 Three things that can improve, shallowest first",
            "13.3 The guardrails",

            // 19.2 from 8.3, whole. Its rows say what each claim source is
            // checked for and how each fails, which is a description of this
            // check's own reconciliation: the catalogue and the matrix in both
            // directions, a figure nothing placed, an expectation reporting FAIL
            // or UNEXAMINED. It was due at 8.3 while its last row, the candidate
            // register, named a table no migration had created, because a table
            // whose rows cannot all be put to something is a table claimed before
            // most of it exists.
            "19.2 What the harness checks",

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

    // A row that names a check in its own words is a row saying that check
    // asserts part of it. Where the verdict names another check, the named one
    // declares reach over the row, holds it with a test of its own that runs, is
    // named in the verdict's note, and fails the row where it fails, which is how
    // section 17's version bound passed at 8.6 naming a recorded night
    // `nightly-cost` did not run.
    [Fact]
    public void EveryCheckARowNamesInAnyFormIsTheVerdictsCheckOrOneThatHoldsTheRowWithATestOfItsOwn()
    {
        var coverage = CoverageReported.Coverage();
        var roster = coverage.Select(check => check.Check).ToHashSet(StringComparer.Ordinal);
        var tables = ArchitectureTables.In(File.ReadAllText(Repository.Architecture));
        var claims = Report().Claims;
        var reaches = CheckReaches.All();

        var named = ArchitectureTables.ChecksNamed(tables, roster);

        // The population, stated in advance: one row names a check beside its verdict's today.
        Assert.True(
            named.Count(row => claims.FirstOrDefault(claim => claim.Table == row.Heading && claim.Subject == row.Subject)?.By != row.Check) >= 1,
            $"Read {named.Count} row(s) naming a check, none of them beside a verdict by another check.");

        Assert.Empty(ChecksTheVerdictLeavesUnsaid(named, claims, reaches, coverage));

        // The reader finds a check named in code markup, bare, and in backticks, and
        // not a longer word that begins with one.
        Assert.Equal(
            [("17. Limits", "A component", "bar-bounds"), ("17. Limits", "A figure", "bar-bounds"), ("17. Limits", "A bound", "nightly-cost")],
            ArchitectureTables.ChecksNamed(
                ArchitectureTables.In(
                    "<h2>17. Limits</h2><table><tr><th>Name</th><th>What</th></tr>" +
                    "<tr><td>A component</td><td>held by <code>bar-bounds</code></td></tr>" +
                    "<tr><td>A figure</td><td>held by bar-bounds and nothing else</td></tr>" +
                    "<tr><td>A bound</td><td>held by `nightly-cost`</td></tr>" +
                    "<tr><td>A word</td><td>held by the bar-boundsless reader and nightly-costs</td></tr></table>"),
                roster));

        // Each fault, over constructed reaches, claims and coverage, by its exact message.
        const string Heading = "17. Limits";

        CheckReach Declared(IReadOnlyDictionary<string, string> held, params string[] subjects) =>
            new("nightly-cost", [], [.. subjects.Select(subject => CheckReach.Key(Heading, subject))]) { Held = held };

        var holding = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CheckReach.Key(Heading, "A bound")] = nameof(NightlyCost.ARecordedNightAtOneRuleVersionAndAtTheFullestRegisterMakesTheSameRequestsAndNoModelCall),
            [CheckReach.Key(Heading, "A stray")] = "NoSuchTest",
        };

        Claim[] verdicts =
        [
            new(Heading, "A bound", Verdict.Pass, "the caps refused, and `nightly-cost` over a recorded night", "rule-versions-scored"),
            new(Heading, "A silent bound", Verdict.Pass, "the caps refused", "rule-versions-scored"),
            new(Heading, "A stray", Verdict.Pass, "the caps refused, and `nightly-cost` too", "rule-versions-scored"),
            new(Heading, "An undeclared bound", Verdict.Pass, "the caps refused, and `nightly-cost` too", "rule-versions-scored"),
            new(Heading, "An unheld bound", Verdict.Pass, "the caps refused, and `nightly-cost` too", "rule-versions-scored"),
            new(Heading, "Its own", Verdict.Pass, "whatever the note says", "nightly-cost"),
        ];

        var reach = Declared(
            new Dictionary<string, string>(holding, StringComparer.Ordinal) { [CheckReach.Key(Heading, "A silent bound")] = holding[CheckReach.Key(Heading, "A bound")] },
            "A bound",
            "A silent bound",
            "A stray",
            "An unheld bound");

        IReadOnlyList<(string Heading, string Subject, string Check)> rows =
        [
            (Heading, "A bound", "nightly-cost"),
            (Heading, "Its own", "nightly-cost"),
            (Heading, "A missing row", "nightly-cost"),
            (Heading, "An undeclared bound", "nightly-cost"),
            (Heading, "An unheld bound", "nightly-cost"),
            (Heading, "A stray", "nightly-cost"),
            (Heading, "A silent bound", "nightly-cost"),
        ];

        Assert.Equal(
            [
                "A missing row names nightly-cost and carries no verdict",
                "An undeclared bound names nightly-cost, which does not declare reach over the row, and its verdict by rule-versions-scored passes it on what rule-versions-scored reached",
                "An unheld bound names nightly-cost, which names no test of its own holding the row",
                "A stray names nightly-cost, and NoSuchTest is not a test of nightly-cost that runs",
                "A silent bound names nightly-cost, and its verdict's note does not say what nightly-cost asserts",
            ],
            ChecksTheVerdictLeavesUnsaid(rows, verdicts, [reach], coverage));
    }

    // A name written as a check in a table is a claim that the check exists. One the
    // roster does not carry is a misspelling or a check never built, and either is a
    // row asserting more than anything holds.
    [Fact]
    public void NoTableRowNamesACheckTheRosterDoesNotCarry()
    {
        var roster = CoverageReported.Coverage().Select(check => check.Check).ToHashSet(StringComparer.Ordinal);
        var document = File.ReadAllText(Repository.Architecture);

        Assert.True(
            CheckShapedNamesIn(document).Count >= 1,
            $"Read {CheckShapedNamesIn(document).Count} check-shaped name(s) in the document's tables, expected at least 1.");
        Assert.Empty(CheckShapedNamesNobodyRosters(document, roster));

        Assert.Equal(
            [
                "nightly-costs is written as a check in a table and the roster carries no such check",
                "rule-version-scored is written as a check in a table and the roster carries no such check",
            ],
            CheckShapedNamesNobodyRosters(
                "<table><tr><td>A</td><td>`nightly-costs` and `nightly-cost`</td></tr></table>" +
                "<table><tr><td>B</td><td><code>rule-version-scored</code></td></tr></table>" +
                "<p>`outside-a-table`</p>",
                roster));
    }

    static IReadOnlyList<string> CheckShapedNamesIn(string document) =>
    [
        .. Regex.Matches(document, @"<table[^>]*>.*?</table>", RegexOptions.Singleline)
            .SelectMany(table => Regex.Matches(table.Value, @"(?:<code>|`)([a-z]+(?:-[a-z]+)+)(?:</code>|`)"))
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal),
    ];

    static IReadOnlyList<string> CheckShapedNamesNobodyRosters(string document, IReadOnlySet<string> roster) =>
    [
        .. CheckShapedNamesIn(document)
            .Where(name => !roster.Contains(name))
            .Select(name => $"{name} is written as a check in a table and the roster carries no such check"),
    ];

    // A second check a row names is read by the verdict as its own check is, over
    // the row that names one today and a run in which every other check held.
    [Fact]
    public void AClaimFailsWhereASecondCheckItsRowNamesFails()
    {
        var document = File.ReadAllText(Repository.Architecture);
        var coverage = CoverageReported.Coverage();

        Claim Built(CheckRun nightlyCost) =>
            PhaseReport.Build(
                    ArchitectureTables.In(document),
                    ArchitectureFigures.In(document),
                    NightlyRunSteps.In(document),
                    Fixtures.Of(Repository.Root),
                    coverage,
                    SuiteOutcomes.Of(coverage
                        .Where(check => check.Carrier != CoverageReported.NotDueYet)
                        .ToDictionary(
                            check => check.Check,
                            check => check.Check == "nightly-cost"
                                ? new CheckResult(nightlyCost, "NightlyCost.ARecordedNight", "one request on the version step")
                                : new CheckResult(CheckRun.Passed),
                            StringComparer.Ordinal)))
                .Claims.Single(claim => claim.Table == Scope.LimitsTable && claim.Subject == "Rule versions scored at once");

        var failed = Built(CheckRun.Failed);
        var unrun = Built(CheckRun.DidNotRun);
        var held = Built(CheckRun.Passed);

        Assert.Equal(("rule-versions-scored", "nightly-cost"), (failed.By, string.Join(", ", failed.Also ?? [])));
        Assert.Equal(
            (Verdict.Fail, "`nightly-cost`, which the row names, ran and did not hold, at NightlyCost.ARecordedNight: one request on the version step"),
            (failed.Verdict, failed.Note));
        Assert.Equal(
            (Verdict.Unexamined, "`nightly-cost`, which the row names, did not run in the run this report reads"),
            (unrun.Verdict, unrun.Note));
        Assert.Equal(Verdict.Pass, held.Verdict);
    }

    static IReadOnlyList<string> ChecksTheVerdictLeavesUnsaid(
        IReadOnlyList<(string Heading, string Subject, string Check)> named,
        IReadOnlyList<Claim> claims,
        IReadOnlyList<CheckReach> reaches,
        IReadOnlyList<CheckCoverage> coverage)
    {
        var faults = new List<string>();

        foreach (var (heading, subject, check) in named)
        {
            var claim = claims.FirstOrDefault(one => one.Table == heading && one.Subject == subject);

            if (claim is null)
            {
                faults.Add($"{subject} names {check} and carries no verdict");

                continue;
            }

            if (claim.By == check)
            {
                continue;
            }

            if (reaches.FirstOrDefault(one => one.Check == check) is not { } reach || !reach.Covers(heading, subject))
            {
                faults.Add($"{subject} names {check}, which does not declare reach over the row, and its verdict by {claim.By} passes it on what {claim.By} reached");

                continue;
            }

            if (!reach.Held.TryGetValue(CheckReach.Key(heading, subject), out var test))
            {
                faults.Add($"{subject} names {check}, which names no test of its own holding the row");

                continue;
            }

            var carrier = coverage.FirstOrDefault(one => one.Check == check)?.Carrier;
            var fact = carrier is null || carrier == CoverageReported.NotDueYet
                ? null
                : SuiteOutcomes.CarrierType(carrier).GetMethod(test, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)?.GetCustomAttribute<FactAttribute>();

            if (fact is null || fact.Skip is not null)
            {
                faults.Add($"{subject} names {check}, and {test} is not a test of {check} that runs");

                continue;
            }

            if (!claim.Note.Contains($"`{check}`", StringComparison.Ordinal))
            {
                faults.Add($"{subject} names {check}, and its verdict's note does not say what {check} asserts");
            }
        }

        return faults;
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
        // returned was placed, and the reader matches table elements, so nothing
        // read a figure at all: the population was defined by the thing it was
        // checking. The phase 4 sign-off found one consequence, figure 10.1's
        // rows reached by nothing while the trailing stop rule the code ran
        // drifted from the corpus for a phase.
        //
        // The document draws a figure in either of two forms and this reads both,
        // because a reader that took one form for the whole population would
        // leave the other form out of the count that reports it missing, which is
        // the same defect one level in.
        var figures = ArchitectureFigures.In(File.ReadAllText(Repository.Architecture));

        // Scope, stated in advance: every figure the document draws, box figures
        // and drawn ones together, and the boxes the box figures carry. The boxes
        // carry the property and the figures are the context, so the floor sits
        // on the boxes; the figure floor is what a form dropping out of the
        // reader would fall through.
        var boxes = figures.Sum(figure => figure.Boxes.Count);

        Assert.True(figures.Count >= 8, $"Read {figures.Count} figures, expected at least 8.");
        Assert.True(boxes >= 50, $"Read {boxes} figure boxes, expected at least 50.");

        var placed = Report().Tables.Select(entry => entry.Heading).ToArray();

        Assert.DoesNotContain(figures, figure => !placed.Contains(figure.Id, StringComparer.Ordinal));
    }

    [Fact]
    public void EveryFormAFigureIsDrawnInIsReadAndTheDocumentDrawsInBoth()
    {
        // The floor in the check above counts figures and cannot say which form
        // they took, so a reader that dropped one form would pass on the other
        // form's growth. This is the statement that floor rests on: both forms
        // are read, and the document draws in both, so neither arm is code the
        // floor would never exercise.
        var constructed = ArchitectureFigures.In(
            "<div class=\"fig\"><div class=\"title\">Figure 1.1. A box figure.</div>" +
            "<div class=\"row\"><div class=\"box compute\"><b>A step</b>what it does</div></div></div>" +
            "<figure class=\"fig svgfig\"><svg viewBox=\"0 0 10 10\"><title>A drawing</title></svg>" +
            "<figcaption>Figure 2.1. A drawn figure.</figcaption></figure>");

        Assert.Equal(["Figure 1.1", "Figure 2.1"], constructed.Select(figure => figure.Id).ToArray());
        Assert.Equal(["A step"], constructed[0].Boxes.Select(box => box.Name).ToArray());
        Assert.Empty(constructed[1].Boxes);

        // A drawn figure that names itself nowhere is refused rather than read
        // under a name of the reader's own, because the name is what a placement
        // is keyed on.
        Assert.Throws<InvalidOperationException>(() => ArchitectureFigures.In(
            "<figure class=\"fig svgfig\"><figcaption>A drawing of something.</figcaption></figure>"));

        // The populations of each form in the document, stated in advance.
        var figures = ArchitectureFigures.In(File.ReadAllText(Repository.Architecture));

        Assert.True(
            figures.Count(figure => figure.Boxes.Count > 0) >= 4,
            $"Read {figures.Count(figure => figure.Boxes.Count > 0)} box figure(s), expected at least 4.");

        Assert.True(
            figures.Count(figure => figure.Boxes.Count == 0) >= 4,
            $"Read {figures.Count(figure => figure.Boxes.Count == 0)} drawn figure(s), expected at least 4.");
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

        // The claim count grows as the document is read more completely, so the
        // floor sits far below it and never needs moving. It was 100, and 5.4
        // lowered it to 80 in the same edit that lowered the two out-of-scope
        // floors from 100, which was a sweep over a literal rather than a
        // judgement: this count had gone 203, 226, 234, 237 and was never near
        // either number. The failure message kept saying 100 while the assertion
        // said 80, so the two disagreed until the phase 5 sign-off read them.
        // Restored to 100, and the message is derived from the floor rather than
        // written beside it, because a message stating a second number is the
        // second place one fact lives.
        const int ClaimFloor = 100;

        Assert.True(
            report.Claims.Count >= ClaimFloor,
            $"Read {report.Claims.Count} claims, expected at least {ClaimFloor}.");

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

        Assert.Contains(
            "is not a check the roster in .claude/rules/checks.md carries",
            refusal.Message,
            StringComparison.Ordinal);
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
            .Select(claim => DueNamedBy(claim.Note))
            .ToArray();

        // Context with a non-vacuity guard, and deliberately not a floor that
        // tracks the count.
        //
        // This number falls as the build advances and reaches zero at phase 8 by
        // construction, so no floor under it can be far enough below that
        // ordinary building never moves it, which is the test .claude/rules/checks.md sets for
        // keeping one. It stood at 100, then 80 at 5.4, then 70 at 5.6, and each
        // fall was recorded with the same sentence saying the number is a fact
        // about how far the build has got rather than about the property. That
        // sentence is the rule's own condition for the other branch: such a
        // scope is left without a floor and marked as context. Three sessions
        // wrote the condition and lowered the floor anyway, which is a
        // maintenance edit every phase and never a caught defect.
        //
        // So the guard is one, which is what the number was ever doing here: it
        // stops the two assertions below passing over an empty set. The property
        // is carried by those assertions and not by the size of the population.
        // When phase 8 empties this set the guard fails, which is correct: at
        // that point the two assertions below have nothing to say and the test
        // is what has to change, rather than the number.
        // Phase 8 emptied this set at 8.6, which the comment above said would
        // happen and said what to do about it: the test is what changes rather
        // than the number. So the two properties are asserted over whatever is
        // left, which may be nothing, and the readers behind them are put to
        // constructed points, where the population cannot empty.
        var lost = due.Where(point => !DuePoints.InThePlan(point, plan)).ToArray();
        var landed = due.Where(point => DuePoints.HasLanded(point, progress)).ToArray();

        Assert.True(
            lost.Length == 0 && landed.Length == 0,
            $"Of {due.Length} out-of-scope due point(s), {lost.Length} name a point the plan lacks " +
            $"({string.Join(", ", lost)}) and {landed.Length} name one the record shows as landed " +
            $"({string.Join(", ", landed)}).");

        // The two readers, over points written here. A checkpoint this plan has
        // and this record does not is the shape a live out-of-scope claim takes,
        // and the other two are the faults the assertions above look for.
        const string Plan = "### 9.1 A thing that is built\nIt builds a thing.\n\n";
        const string Record = "### 9.1 - a thing that is built   2026-10-01\nBuilt:      a thing.\n\n";

        Assert.True(DuePoints.InThePlan("9.1", Plan));
        Assert.False(DuePoints.InThePlan("9.9", Plan));
        Assert.False(DuePoints.HasLanded("9.1", string.Empty));
        Assert.True(DuePoints.HasLanded("9.1", Record));
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
            Not a checkpoint entry. It belongs to 3.0.

            ### 1.2 - the one-year backfill
            Built:      the backfill.
            """;

        // The planning pass lands the checkpoint it belongs to and never its
        // phase. Until 7.1 it landed neither, so an obligation owed at 3.0 read
        // as open after 3.0's entry was written exactly as before it.
        Assert.False(DuePoints.HasLanded("phase 3", planningOnly));
        Assert.True(DuePoints.HasLanded("3.0", planningOnly));
        Assert.False(DuePoints.HasLanded("3.1", planningOnly));
        Assert.True(DuePoints.HasLanded("phase 1", planningOnly));

        const string built = """
            ### 3.1 - the indicator engine and the averages on the chart
            Built:      the indicator engine.
            """;

        Assert.True(DuePoints.HasLanded("phase 3", built));
        Assert.True(DuePoints.HasLanded("3.1", built));

        // The number is not what tells them apart. An entry headed with a
        // building checkpoint whose body opens the planning way is not a
        // building entry, and the old matcher had no way to see that at all.
        const string numberedLikeACheckpoint = """
            ### 3.1 - the pass that settles what phase 3 builds against
            Not a checkpoint entry. It belongs to 3.1, which has not landed.
            """;

        Assert.False(DuePoints.HasLanded("phase 3", numberedLikeACheckpoint));
        Assert.False(DuePoints.HasLanded("3.1", numberedLikeACheckpoint));

        // The heading is. A ruling at a planning checkpoint opens the planning
        // way and plans nothing, and a planning entry headed with a building
        // checkpoint, which is how phase 1 was planned, plans no opening
        // checkpoint: each lands neither its checkpoint nor its phase.
        const string ruling = """
            ### 3.0 ruling - an item carried to 3.0, ruled ahead of the pass
            Not a checkpoint entry. It belongs to 3.0, which has not landed.
            """;

        Assert.False(DuePoints.HasLanded("3.0", ruling));
        Assert.False(DuePoints.HasLanded("phase 3", ruling));

        const string plannedUnderABuildingCheckpoint = """
            ### 1.1 planning - the pass that settles what phase 1 builds against
            Not a checkpoint entry. It belongs to 1.1, which has not landed.
            """;

        Assert.False(DuePoints.HasLanded("1.1", plannedUnderABuildingCheckpoint));
        Assert.False(DuePoints.HasLanded("phase 1", plannedUnderABuildingCheckpoint));
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
        var progress = Corpus.Read("docs/PROGRESS.md");
        var built = DuePoints.Built(progress);

        Assert.True(built.Count >= 8, $"Read {built.Count} built checkpoints from PROGRESS, expected at least 8.");

        // The planning half, over the same record. Every phase whose plan has an
        // opening checkpoint and whose building has started has that checkpoint
        // landed, which is the population a reader keyed on a heading could read
        // none of and stay green: a planning entry headed any other way would
        // leave its checkpoint unlanded and every obligation owed at it unchased,
        // which is the defect 7.1 repaired arriving by another route.
        //
        // The population is derived from the plan and the record rather than floored:
        // see `UnreadOpenings` below, whose constructed proof is the test after this one.
        var checkpoints = PlanCheckpoints.All().Select(point => point.Id).ToArray();
        var openings = checkpoints.Where(id => id.EndsWith(".0", StringComparison.Ordinal)).ToArray();
        var started = Started(openings, built);

        // The floor is context: how many phases have started is a fact about how
        // much is built, and it is stated so a reader returning nothing is not
        // read as a corpus with no started phases. The property is the set below.
        Assert.True(started.Count >= 5, $"Read {started.Count} started phases with an opening checkpoint, expected at least 5.");

        var unread = UnreadOpenings(openings, checkpoints, started, built, DuePoints.Planned(progress));

        Assert.True(unread.Count == 0, "Opening checkpoints the record reads wrongly: " + string.Join(", ", unread) + ".");

        Assert.Contains("1.1", built);
        Assert.Contains("1.2", built);

        // The other direction, derived from the record's own headings: a checkpoint every
        // one of whose entries opens as not a checkpoint entry is one the reader never builds.
        var entries = progress.Split("\n### ")
            .Skip(1)
            .Select(entry => (Id: Regex.Match(entry, @"^(\d+\.\d+)").Groups[1].Value, Body: entry[(entry.IndexOf('\n') + 1)..].TrimStart()))
            .Where(entry => entry.Id.Length > 0)
            .ToArray();

        var headed = entries.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);

        var neverBuilt = entries
            .GroupBy(entry => entry.Id, StringComparer.Ordinal)
            .Where(group => group.All(entry => entry.Body.StartsWith(DuePoints.NotACheckpoint, StringComparison.Ordinal)))
            .Select(group => group.Key)
            .ToArray();

        Assert.True(neverBuilt.Length >= 1, $"Read {neverBuilt.Length} checkpoint(s) headed only by entries that are not checkpoint entries, expected at least 1.");
        Assert.DoesNotContain(neverBuilt, built.Contains);
        Assert.DoesNotContain(built, id => !headed.Contains(id));

        // And over a record written here, where a ruling and a checkpoint named only in a body sit beside a building entry.
        Assert.Equal(
            ["9.1"],
            DuePoints.Built(
                "### 9.1 - a thing that is built   2026-10-01\nBuilt:      a thing, ahead of 9.3.\n\n" +
                "### 9.2 ruling - an item carried to 9.2\nNot a checkpoint entry. It belongs to 9.2, which has not landed.\n\n"));
    }

    // The opening checkpoints of phases something other than the opening has built.
    static IReadOnlyList<string> Started(IReadOnlyList<string> openings, IReadOnlyList<string> built) =>
    [
        .. openings.Where(id => built.Any(checkpoint =>
            DuePoints.PhaseOf(checkpoint) == DuePoints.PhaseOf(id) && checkpoint != id)),
    ];

    // Phases are planned and built in order, so every phase before the newest landed opening
    // has started and landed, and that newest one alone may be planned and not started. A
    // reader losing every building entry of the newest phase reads as that state and is not
    // found here.
    internal static IReadOnlyList<string> UnreadOpenings(
        IReadOnlyList<string> openings,
        IReadOnlyList<string> checkpoints,
        IReadOnlyList<string> started,
        IReadOnlyList<string> built,
        IReadOnlyList<string> planned)
    {
        var newest = openings
            .Where(id => DuePoints.HasLanded(id, built, planned))
            .OrderBy(DuePoints.Order)
            .LastOrDefault();

        bool BuildsItsPhase(string opening) =>
            checkpoints.Any(id => id != opening
                && DuePoints.PhaseOf(id) == DuePoints.PhaseOf(opening)
                && built.Contains(id, StringComparer.Ordinal));

        return
        [
            .. openings.Where(id => started.Contains(id, StringComparer.Ordinal)
                ? !DuePoints.HasLanded(id, built, planned)
                : BuildsItsPhase(id) || (newest is not null && DuePoints.Compare(id, newest) < 0)),
        ];
    }

    [Fact]
    public void AnOpeningCheckpointReadWronglyIsFoundWhicheverReaderLosesItsPhase()
    {
        string[] openings = ["2.0", "3.0", "4.0"];
        string[] checkpoints = ["2.0", "2.1", "3.0", "3.1", "4.0", "4.1"];
        string[] planned = ["2.0", "3.0", "4.0"];

        IReadOnlyList<string> Unread(string[] started, string[] built, string[] landedAsPlanned) =>
            UnreadOpenings(openings, checkpoints, started, built, landedAsPlanned);

        // Every phase built and planned, and the newest planned with nothing built yet.
        Assert.Empty(Unread(["2.0", "3.0", "4.0"], ["2.1", "3.1", "4.1"], planned));
        Assert.Empty(Unread(["2.0", "3.0"], ["2.1", "3.1"], planned));

        // A phase before the newest that nothing reads as built, with its opening landed and
        // with it lost as well.
        Assert.Equal(["3.0"], Unread(["2.0", "4.0"], ["2.1", "4.1"], planned));
        Assert.Equal(["3.0"], Unread(["2.0", "4.0"], ["2.1", "4.1"], ["2.0", "4.0"]));

        // A started phase whose opening the planning reader does not land.
        Assert.Equal(["3.0"], Unread(["2.0", "3.0", "4.0"], ["2.1", "3.1", "4.1"], ["2.0", "4.0"]));

        // A started set counting one phase fewer than the record builds, the newest or one
        // before it.
        Assert.Equal(["4.0"], Unread(["2.0", "3.0"], ["2.1", "3.1", "4.1"], planned));
        Assert.Equal(["3.0"], Unread(["2.0", "4.0"], ["2.1", "3.1", "4.1"], planned));

        // An opening the plan has beyond the newest landed one is a phase not reached.
        Assert.Empty(Unread(["2.0"], ["2.1"], ["2.0"]));
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
        // A deferred claim says where it ends, which the writer and the readers are shown to agree on below.
        var silent = SilentOutOfScope(report.Claims);

        Assert.True(
            silent.Count == 0,
            $"{silent.Count} of {outOfScope.Length} out-of-scope claim(s) do not say where they end: " +
            string.Join("; ", silent.Select(claim => claim.Subject)));

        // The note's writer and both its readers, over a step the step map answers and no verdict reaches.
        const string Deferred = "Load index membership for a night no step of the document describes";

        var written = Scope.For(NightlyRunSteps.Heading, Deferred);

        Assert.Equal(Verdict.OutOfScope, written.Verdict);
        Assert.Empty(SilentOutOfScope([new Claim(NightlyRunSteps.Heading, Deferred, written.Verdict, written.Note, written.By)]));
        Assert.Equal(Scope.Resolve(NightlyRunSteps.Heading, Deferred).Due, DueNamedBy(written.Note));
        Assert.Single(SilentOutOfScope([new Claim(NightlyRunSteps.Heading, Deferred, Verdict.OutOfScope, "reached by nothing and said so", string.Empty)]));
    }

    // The out-of-scope claims whose note does not say where they end.
    internal static IReadOnlyList<Claim> SilentOutOfScope(IEnumerable<Claim> claims) =>
        [.. claims.Where(claim => claim.Verdict == Verdict.OutOfScope && !claim.Note.Contains("until ", StringComparison.Ordinal))];

    // The due point an out-of-scope note names, read the way a person reads it.
    internal static string DueNamedBy(string note) =>
        note.LastIndexOf("until ", StringComparison.Ordinal) is var at and >= 0 ? note[(at + "until ".Length)..].Trim() : string.Empty;

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

        // The prefix-keyed map is excluded, and what makes it excludable is
        // asserted rather than taken from the label: every key has to be a
        // strict prefix of a step in section 14 and equal to none of them,
        // which is what means the resolver never asks DueFor with the key. An
        // exclusion that removed nothing, or one that grew to hold a real
        // subject, fails here rather than narrowing the check quietly.
        var document = Corpus.Read("docs/ARCHITECTURE.html");
        var prefixKeyed = Scope.ResidualPrefixSubjects();
        var steps = NightlyRunSteps.In(document);

        Assert.True(
            prefixKeyed.Count >= 10,
            $"Read {prefixKeyed.Count} prefix-keyed residual subjects, expected at least 10.");

        var notAPrefix = prefixKeyed
            .Where(key => !steps.Any(step =>
                step.StartsWith(key, StringComparison.Ordinal) && step.Length > key.Length))
            .ToArray();

        Assert.True(
            notAPrefix.Length == 0,
            "These are excluded from the shadow check as prefix keys and are not a strict prefix " +
            "of any step in section 14, so the exclusion is covering a real subject: " +
            string.Join("; ", notAPrefix));

        // The second exclusion, and the same discipline. A fixture row's key is
        // the name of a file, so every one has to be a row of section 19.1's
        // own table. A key that is not a row there is a subject hiding in the
        // exclusion, and the floor stops the exclusion emptying.
        var fileNamed = Scope.ResidualFileNameSubjects();

        var fixtureRows = ArchitectureTables.In(document)
            .Where(table => table.Heading == Scope.FixtureTable)
            .SelectMany(table => table.Body)
            .Where(row => row.Count > 1 && row[0].Length > 0)
            .Select(row => row[0])
            .ToArray();

        Assert.True(
            fileNamed.Count >= 10,
            $"Read {fileNamed.Count} file-named residual subjects, expected at least 10.");

        Assert.True(
            fixtureRows.Length >= 14,
            $"Read {fixtureRows.Length} rows in {Scope.FixtureTable}, expected at least 14.");

        var notAFixtureRow = fileNamed
            .Where(key => !fixtureRows.Contains(key, StringComparer.Ordinal))
            .ToArray();

        Assert.True(
            notAFixtureRow.Length == 0,
            "These are excluded from the shadow check as fixture file names and are not a row of " +
            Scope.FixtureTable + ", so the exclusion is covering a real subject: " +
            string.Join("; ", notAFixtureRow));

        var shadowing = Scope.ResidualSubjects()
            .Where(subject => !exceptions.Contains(subject, StringComparer.Ordinal))
            .Where(subject => !prefixKeyed.Contains(subject, StringComparer.Ordinal))
            .Where(subject => !fileNamed.Contains(subject, StringComparer.Ordinal))
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

        // The split by origin is the property, and an empty set satisfies it as 0 of 0,
        // so the derivation and Resolve are put to constructed input below.
        Assert.Equal(
            outOfScope.Length,
            plan + screens + written + excepted);

        // The derivation, over a plan written here. Two checkpoints, and the
        // earlier one answers, which is the rule: a subject named twice is first
        // owed where it first appears.
        var constructed = PlanCheckpoints.In(
            "### 9.1 A thing that is built\nIt names the widget register and builds it.\n\n" +
            "### 9.2 Another thing\nIt names the widget register again, and the sprocket table.\n\n",
            floor: 2);

        Assert.Equal("9.1", PlanCheckpoints.DueFor("widget register", constructed));
        Assert.Equal("9.2", PlanCheckpoints.DueFor("sprocket table", constructed));
        Assert.Null(PlanCheckpoints.DueFor("a subject this plan never names", constructed));

        // And that Resolve still reaches it, over a subject no written map in
        // Scope carries and the real plan does. Without this half the reader
        // above could go on answering while nothing asked it.
        var throughResolve = Scope.Resolve(Scope.StoresTable, "Candidate register");

        Assert.Equal(DueOrigin.Plan, throughResolve.Origin);
        Assert.Equal("8.3", throughResolve.Due);

        Assert.True(
            Count(DueOrigin.Nothing) == 0,
            $"{outOfScope.Length} claims are out of scope: {plan} from BUILD_PLAN, {screens} from " +
            $"section 15, {written} written into Scope and {excepted} declared exceptions.");
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

        // Stated in advance: seven tables, 41 rows, 104 claim subjects. It was
        // 37 and 100 until 6.0, which added four rows for states section 15
        // promised and gave no region: research not yet written, research stale
        // and research paused on the name screen, each of which section 4's
        // key, figure 12.1 and three section 18 rows all describe, and the
        // overnight queue's region on the run page, which section 18 says
        // states whether the queue ran and on which night. The
        // Level chart row decomposes into its four elements, and 5.0 decomposed
        // three more per surface, being the universe screen's sector strip and
        // table, whose listing halves cannot exist until 5.4 creates that
        // store, and the run page's reason record, whose counts are 5.6's and
        // whose verdicts need resolved setups. The phase 5 sign-off split the
        // run page's stale-and-failed region into its four parts, two of them
        // phase 6's. A run finding none would otherwise pass both directions
        // over an empty set.
        // 42 from 41 at the 7.0 ruling, which added the name screen's line for a name whose
        // stored series is suspect. 43 at the 5.8 correction that listed the names holding
        // research on the universe screen. 44 at the one that drew a name's listing history,
        // and 45 at the one that watched a pass the page started. 45 still at the correction
        // that removed the provenance footer and named the sections left out, one row for one,
        // and 46 at the one that put a contents at the head of a name's page.
        // 47 at the one that stated how far each band sits from the close.
        // 48 at the one that drew the two cases as two labelled halves, and 49 at the one that
        // drew the risks one part to a risk. 55 at 9.0, which added what a row on tonight's list
        // says about research and what it can ask for, and the queue screen's four regions.
        // 57 at 10.1, which added the run page's region measuring the order tonight's list is drawn in.
        // 58 at 10.2, which added the run page's region holding each registered candidate's record.
        // 62 at 11.0's document pass, which added the name screen's four parts phase 11 builds.
        // 63 at 11.2, the queue page's row stating when each request will be written.
        // 64 at 11.3, tonight's row stating the report's state.
        // 65 at 11.9, the run page's region stating each reason's share of the index against its target.
        // 67 at 12.1, the name page's swing readings and the run page's market row.
        // 69 at 12.2, the name page's gates and the run page's swing filter funnel.
        // 71 at 12.3, the Calibration region's three rows where 11.9's region was one.
        // 72 at 12.4, the run page's shape proposal.
        // 74 at 12.6, tonight's evening before the switch and the run page's list from night to night.
        // 76 at 12.7, the run page's edge clock and near misses.
        // 80 at 5.8's correction that drew the watch list page's four rows.
        // 81 at 3.4's correction, the name page's level evidence.
        // 82 at 5.8's correction that took the name page's listing history off it and drew the numbers'
        // snapshot and Regenerate Report.
        Assert.Equal(82, screensTables.Sum(table => table.Body.Count(row => row.Count > 0)));

        // 110 from 104 at 6.1, which decomposed the name screen's fact strip into
        // the seven parts its row enumerates. The row's own subject goes with the
        // decomposition, so seven arrive and one leaves. Two of the seven are why
        // the strip is owed at 6.1 rather than in phase 3: market capitalisation
        // and the multiples come from the fundamentals and no computed table holds
        // either, so a strip drawn earlier would have been five parts of seven with
        // a verdict covering all of them.
        // 114 from 110 at 6.5, which decomposed section 15.9's two research-state
        // rows into three parts each. Two leave and six arrive, and the parts owed at
        // 6.8 arrive with their own due points rather than inheriting the row's.
        // 116 from 114 at 6.6, which decomposed the provenance footer into the three
        // kinds of part its row enumerates. One leaves and three arrive. 117 at 6.7,
        // which decomposed research paused into its line and its sections. 121 at the 7.0
        // ruling: the name screen's line for a suspect name arrives as the three parts its
        // row enumerates, and tonight's list row gains the line it draws beside the name.
        // 122 at 8.1, the run page's reason records row gaining the never-entered count.
        // 124 at 8.4, the run page's shadow candidates row decomposed into the three
        // things it states: one leaves and three arrive. 8.0 predicted the row as one
        // claim ending here, and the harness reads it as three, because a row passes
        // for what it says and a single verdict over three statements passes when one
        // is drawn and two are not.
        // 127 at 8.5, section 15.11's "at or above the minimum" row decomposed into
        // the three figures it names and the claim that they arrive together: one
        // leaves and four arrive.
        // 128 at the 5.8 correction that listed the names holding research, one row and one
        // claim, 129 at the one that drew a name's listing history, the same, and 130 at the
        // one that watched a pass the page started. 128 at the one that stopped the page
        // drawing a refused draft: the provenance footer's row leaves with its three parts,
        // its promise held by the stamp on each card it described, and the lines naming what
        // was left out arrive as the one row that claims them.
        // 129 at the correction that put a contents at the head of a name's page, one row and
        // one claim: a reader reaching a section is a claim about a surface.
        // 130 at the one that stated how far each band sits from the close.
        // 131 at the one that drew the two cases as two labelled halves, and 132 at the one that
        // drew the risks one part to a risk. 138 at 9.0, which added two rows to tonight's list
        // and the queue screen's four, and 139 at 9.4, which states which lane would write one.
        // 146 at 10.1: tonight's list row gains the reward to risk it draws, and the run page's
        // measure of the list's order arrives as the six parts its row enumerates.
        // 156 at 10.2, the run page's candidates' record region arriving as the ten parts its row
        // enumerates, each a thing a reader sees or a thing the region refuses to draw.
        // 158 at the 5.8 correction that numbered tonight's list, its row gaining the rows'
        // places and the line counting them.
        // 162 at 11.0's document pass, the name screen's four parts arriving as a row and a claim
        // each: a move beside its group, the peers, the earnings reactions and the dividend.
        // 163 at 11.2, the queue page's row stating when each request will be written.
        // 164 at 11.3, tonight's row stating the report's state.
        // 173 at 11.6, the name screen's peers row read as the ten parts it names where it was
        // one claim: its place, its population, its order, that it ranks none, its five
        // columns and its key. 183 at 11.7, the earnings reactions row read as its eleven parts,
        // and 189 at 11.8, the dividend row read as its seven. 195 at 11.9, the run page's region
        // stating each reason's share of the index against its target, read as its six parts.
        // 208 at 12.1: the night header's breadth, the universe table's four new columns, the name
        // page's readings as their five parts and the run page's market row as its three.
        // 216 at 12.2: the name page's gates as their four parts and the run page's funnel as its four.
        // 223 at 12.3: the Calibration region's shape clock as its seven parts, its sentence as one and its
        // trigger lines as their five, where 11.9's region was six.
        // 226 at 12.4: the run page's shape proposal as its three parts.
        // 235 at 12.6: the night header's count the swing filter listed, the list's rule line and gates,
        // the evening before the switch as its three parts and the run page's overlap as its three.
        // 245 at 12.7: the edge clock as its six parts and the near misses as their four.
        // 261 at 5.8's correction: the watch list page's four rows as the sixteen parts they state.
        // 262 at 3.4's correction, the name page's level evidence.
        // 263 at the 5.8 correction that laid the report out to read at a glance: the listing
        // history leaves and the numbers' snapshot and the Regenerate Report control arrive.
        Assert.Equal(263, inDocument.Length);

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

        // An exact count rather than a floor, so a decomposition added without being argued
        // for fails here; the argument for each is its checkpoint's entry.
        // 152 from 142 at 10.2, the run page's candidates' record region read as the ten parts
        // its row enumerates. 154 at the 5.8 correction that numbers tonight's list, the list
        // read as its two new parts, the rows' places and the line counting them. 164 at 11.6,
        // the name screen's peers row read as its ten parts, and 175 at 11.7, its earnings
        // reactions row read as its eleven, and 182 at 11.8, its dividend row read as its seven.
        // 188 at 11.9, the run page's region stating each reason's share against its target read as its six.
        // 201 at 12.1: the night header's breadth, the universe table's four new columns, the name
        // page's readings as their five parts and the run page's market row as its three.
        // 209 at 12.2: the name page's gates as their four parts and the run page's funnel as its four.
        // 215 at 12.3: the Calibration region's shape clock as its seven parts and its trigger lines as
        // their five, where 11.9's region was six.
        // 218 at 12.4: the run page's shape proposal as its three parts.
        // 227 at 12.6: the night header's count the swing filter listed, the list's rule line and gates,
        // the evening before the switch as its three parts and the run page's overlap as its three.
        // 237 at 12.7: the edge clock as its six parts and the near misses as their four.
        // 252 at 5.8's correction: three of the watch list page's rows as the fifteen parts they state.
        Assert.Equal(252, checkedElements);
    }

    // Every part a row enumerates, read off the row rather than chosen by the
    // reader.
    //
    // The fifth phase 5 sign-off review's blocking finding. A row's parts were
    // whatever `Scope.Elements` named, so a clause the reader left out had no
    // verdict of its own while its row read PASS: 15.7's list states a day
    // change, a trend state in a word and the distance row mark, the page draws
    // name, close and reasons, and the row passed whole. That is an unexamined
    // claim wearing a verdict, and it is the population-from-the-document
    // promise broken at the level below the row.
    //
    // An enumeration is a run of three or more comma-separated items, which is
    // how these rows list what a region holds. A two-item list joined by "and"
    // is outside what this reader reaches and is stated as its scope: the rows
    // that carry one are decomposed by hand, and `EveryDecomposedElementIsNamedByTheRowItDecomposes`
    // holds those parts to the row's own words in the other direction.
    internal static IReadOnlyList<string> EnumeratedParts(string description) =>
        [.. ItemsIn(description).Where(item => !RationaleParts.Contains(item))];

    // The run's items before the rationale set is taken out of them, which is
    // what makes that set assertable in both directions: a stated exemption has
    // to name something this reader actually picks up, and eight of the eleven
    // named nothing until the sixth phase 5 sign-off review counted them.
    internal static IReadOnlyList<string> ItemsIn(string description)
    {
        var items = new List<string>();

        foreach (Match run in Regex.Matches(description, @"(?:[^,.;:|]+,\s+){2,}(?:and\s+|or\s+)?[^,.;:|]+"))
        {
            foreach (var item in run.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var named = Regex.Replace(item, @"^(?:and|or|with|so|then|being)\s+", string.Empty).Trim();

                if (named.Length > 0)
                {
                    items.Add(named);
                }
            }
        }

        return items;
    }

    // Every item the reader picks up anywhere in section 15, which is the
    // population a stated exemption has to be drawn from.
    static IReadOnlyList<string> EveryItemInSectionFifteen()
    {
        var tables = ArchitectureTables.In(File.ReadAllText(Repository.Architecture));

        return
        [
            .. Scope.ScreensTables
                .SelectMany(heading => Assert.Single(tables, candidate => candidate.Heading == heading).Body)
                .Where(row => row.Count > 1)
                .SelectMany(row => ItemsIn(row[1])),
        ];
    }

    [Fact]
    public void EveryStatedRationaleItemIsOneTheReaderActuallyPicksUp()
    {
        // The exemption set stated in both directions, which is the discipline
        // `price-storage-form` applies to its cast sites and which this set did
        // not have. Eight of its eleven entries named nothing the reader
        // produces, and four of those carried a vertical bar, which the item
        // class excludes, so they could not have matched any item at all. A
        // stated set whose entries never fire is a set that looks argued and
        // exempts nothing, and the next clause it would wrongly exempt is
        // invisible until it arrives.
        //
        // The other direction, that every item this set holds is reasoning
        // rather than a part, is the argument beside each entry and is not
        // assertable. What is assertable is that each one is real.
        var items = EveryItemInSectionFifteen();

        Assert.NotEmpty(items);

        var dead = RationaleParts
            .Where(stated => !items.Contains(stated, StringComparer.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            dead.Length == 0,
            "These rationale exemptions name nothing the reader picks up, so they exempt nothing: " +
            string.Join("; ", dead) +
            ". An exemption is stated for an item the reader produces, or it is not stated at all.");
    }

    // The items a run picks up that are a row's reasoning rather than a part of
    // what it draws. Stated as a set and argued, the way `price-storage-form`
    // states its cast sites, because a reader over prose cannot tell a clause
    // that says what is drawn from one that says why: each of these is a
    // subordinate clause of the sentence before it, and none of them is a thing
    // a page can be asked to show.
    // Three, and they were eleven until the sixth phase 5 sign-off review counted
    // which of them fire. Eight named nothing this reader picks up: four carried
    // a vertical bar, which the item class excludes, so they could not have
    // matched any item at all, and the other four were phrases from cells whose
    // runs the reader does not reach. A stated set whose entries never fire
    // looks argued and exempts nothing, and the clause it would wrongly exempt
    // is invisible until it arrives.
    // `EveryStatedRationaleItemIsOneTheReaderActuallyPicksUp` holds the set to
    // what the document produces from now on.
    static readonly HashSet<string> RationaleParts = new(StringComparer.OrdinalIgnoreCase)
    {
        // 15.8's table: why the sort order is what it is.
        "the top is what nearly fired",

        // 15.10's operational header: where the detail is drawn, which the
        // part before it already claims.
        "in a cell rather than behind a pointer",

        // 15.10's harness: how the three counts are drawn, which the counts
        // themselves carry.
        "each separately",
    };

    // The items a row states as one phrase and the elements that decompose them,
    // stated rather than matched.
    //
    // The sixth phase 5 sign-off review's second blocking finding. The matcher
    // asked whether a subject contained the part or the part contained the
    // subject's tail, so any new clause whose wording happens to contain an
    // existing element was absorbed by it and passed with no verdict and no
    // complaint. "the day change arrow" added to 15.7's list row went green
    // against the element "day change". The check was not a tautology, but the
    // property it states held only for clauses that avoid the vocabulary already
    // there, and the whole phase 5 sign-off rests on that property.
    //
    // A part is covered now where an element equals it, and otherwise only where
    // this map says which elements decompose it. Six entries, each because the
    // document states in one phrase what the row is decomposed into more finely:
    // a hand decomposition is allowed and is written down, and a clause nobody
    // has decomposed is uncovered rather than absorbed.
    static readonly Dictionary<string, string[]> PartDecompositions = new(StringComparer.OrdinalIgnoreCase)
    {
        // 15.4's app row ends "filters and selection", which is two of the four
        // things the row names and one item to a reader that splits on commas.
        ["filters and selection"] = ["filters", "selection"],

        // 15.5's level chart names four marks in three phrases: the first
        // carries the candles and the bands it shades behind them, and the other
        // two are one element each with the phrase's qualifying tail.
        ["Daily candles with the level bands shaded behind them"] = ["candles", "the level bands"],
        ["the moving averages drawn"] = ["the moving averages"],
        ["a volume pane beneath on a shared time axis"] = ["a volume pane"],

        // 15.10's stale-and-failed row states two of its four parts with what
        // each carries beside it, which is the part and its own detail rather
        // than two parts.
        ["the stage a night stopped on with its outcome and the reason"] = ["the stage a night stopped on"],
        ["documents refused by admissibility with the category that refused each"] = ["documents refused by admissibility"],
    };

    // Whether a row's elements cover one of its stated parts.
    //
    // Equality, or a stated decomposition naming elements the row has. Never
    // containment in either direction, which is what let a new clause be
    // absorbed by an element whose wording it happens to hold.
    internal static bool Covers(IReadOnlyList<string> elements, string part) =>
        elements.Any(element => element.Equals(part, StringComparison.OrdinalIgnoreCase))
        || (PartDecompositions.TryGetValue(part, out var named)
            && named.All(element => elements.Any(held => held.Equals(element, StringComparison.OrdinalIgnoreCase))));

    [Fact]
    public void ADuePointInsideADetailedPhaseNamesACheckpointThatExists()
    {
        // The ruling the sixth phase 5 sign-off review handed down, asserted
        // both ways. The reader accepted the phase alone for every due point, so
        // "5.8" was in the plan before 5.8 was written and an obligation could be
        // owed at a checkpoint nobody had created.
        // see: A due point names a checkpoint that exists wherever its phase has been detailed
        //
        // Over constructed plan text, because the property is about a phase with
        // detail and a phase without one, and the real plan holds only the first
        // kind today. The phases here are not this project's.
        const string plan =
            "## Phase 6: research\n### 6.0 Planning\n### 6.1 The fetcher\n## Phase 9: later\n";

        Assert.True(DuePoints.InThePlan("6.1", plan));
        Assert.False(DuePoints.InThePlan("6.4", plan));

        // A phase with no checkpoints written takes its points on the phase
        // alone, which is the case .claude/rules/checks.md argues for: a later phase gets its
        // detail at the previous phase's sign-off, and a roster row has to be
        // able to name a check that starts there.
        Assert.True(DuePoints.InThePlan("9.2", plan));
        Assert.True(DuePoints.InThePlan("phase 9", plan));

        // A phase the plan does not have at all is in it under neither reading.
        Assert.False(DuePoints.InThePlan("4.1", plan));

        // And against the plan this repository actually carries: every phase
        // through 7 is detailed, so a checkpoint nobody has written is refused
        // where before it passed on its phase.
        var carried = Corpus.Read("docs/BUILD_PLAN.md");

        Assert.True(DuePoints.InThePlan("6.1", carried));
        Assert.False(DuePoints.InThePlan("5.9", carried));
        Assert.False(DuePoints.InThePlan("6.99", carried));
    }

    [Fact]
    public void AClauseIsNotCoveredByAnElementWhoseWordingItMerelyContains()
    {
        // The permanent proof of the sixth phase 5 sign-off review's second
        // blocking finding, over constructed text rather than the document,
        // because the demonstration that found it was a clause added to
        // ARCHITECTURE by hand and reverted.
        //
        // The matcher asked whether a subject contained the part or the part
        // contained the subject's tail. Under it, every assertion below except
        // the first two passes, and a new clause enters section 15 with no
        // verdict, no complaint and a green run.
        string[] elements = ["name", "close", "day change", "the reasons"];

        Assert.True(Covers(elements, "day change"));
        Assert.True(Covers(elements, "the reasons"));

        // The clause the review added to 15.7's list row. It holds an element's
        // whole wording and is not that element.
        Assert.False(Covers(elements, "the day change arrow"));

        // And the other direction, which the old matcher also accepted: a part
        // an element contains is not that element either.
        Assert.False(Covers(elements, "day"));
        Assert.False(Covers(elements, "reasons"));

        // A clause sharing no wording was refused before and still is, which is
        // the case that made the check look like it worked.
        Assert.False(Covers(elements, "a short interest badge"));

        // A stated decomposition covers its phrase, and only where the row holds
        // every element that decomposition names. A map entry cannot licence a
        // part on a row that does not carry the elements behind it.
        Assert.True(Covers(["filters", "selection"], "filters and selection"));
        Assert.False(Covers(["filters"], "filters and selection"));
        Assert.False(Covers(["selection"], "filters and selection"));
    }

    [Fact]
    public void EveryStatedPartDecompositionNamesAPhraseTheDocumentStates()
    {
        // The decomposition map in the direction that can go stale. An entry
        // keyed on a phrase the document no longer holds licences nothing and
        // reads as though it does, which is the shape the rationale set was in
        // when this review counted it.
        //
        // The other direction, that each entry's elements are the right
        // decomposition of its phrase, is the argument written beside it and is
        // not assertable. What is assertable is that the phrase is real.
        var items = EveryItemInSectionFifteen();

        Assert.NotEmpty(items);

        var stale = PartDecompositions.Keys
            .Where(phrase => !items.Contains(phrase, StringComparer.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            stale.Length == 0,
            "These stated decompositions name a phrase no section 15 row states: " +
            string.Join("; ", stale) +
            ". A decomposition is stated for a phrase the document holds, or it is not stated at all.");

        // And every element a decomposition names is one some row is decomposed
        // into, so an entry cannot invent an element to cover a phrase with.
        var elements = Scope.DecomposedRows().SelectMany(Scope.ElementsOf).ToArray();

        var invented = PartDecompositions.Values
            .SelectMany(named => named)
            .Where(element => !elements.Contains(element, StringComparer.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            invented.Length == 0,
            "These stated decompositions name an element no row is decomposed into: " +
            string.Join("; ", invented) + ".");
    }

    [Fact]
    public void EveryPartASectionFifteenRowEnumeratesHasItsOwnVerdict()
    {
        var tables = ArchitectureTables.In(File.ReadAllText(Repository.Architecture));
        var uncovered = new List<string>();
        var checkedParts = 0;

        foreach (var heading in Scope.ScreensTables)
        {
            var table = Assert.Single(tables, candidate => candidate.Heading == heading);

            foreach (var row in table.Body.Where(row => row.Count > 1))
            {
                var subjects = Scope.SubjectsOf(heading, row[0]);

                // A row nothing claims yet says nothing yet. Its parts are owed
                // with it, at the point its own due point names.
                if (!PartsAreOwedFor(subjects.Select(subject => Scope.For(heading, subject).Verdict)))
                {
                    continue;
                }

                foreach (var part in EnumeratedParts(row[1]))
                {
                    checkedParts++;

                    if (!Covers([.. subjects.Select(subject => subject[(subject.IndexOf(',') + 1)..].Trim())], part))
                    {
                        uncovered.Add($"{heading} | {row[0]}: {part}");
                    }
                }
            }
        }

        Assert.True(
            uncovered.Count == 0,
            "These parts are stated by a row a verdict passes and have no verdict of their own: " +
            string.Join("; ", uncovered) +
            ". A row passes for what it says, so every part it enumerates is a claim.");

        Assert.True(checkedParts >= 40, $"Read {checkedParts} enumerated parts, expected at least 40.");
    }

    [Fact]
    public void TheInadmissibleDocumentRowsPartsAreReadOffItsOwnWordsInBothDirections()
    {
        // The obligation 6.0 filed against 6.3, discharged. Section 19.1's row
        // names six kinds the test refuses and carried one verdict over all of
        // them, so five could have been missing and the row would still have
        // passed, which is the fault the fifth phase 5 sign-off review found on
        // section 15's rows.
        //
        // One direction is the shared reader above: every element a decomposition
        // names appears in the row it decomposes. This is the other, and it is the
        // one that matters here, because a decomposition into four of six would
        // pass the first: every part the row's own words enumerate is a claim.
        //
        // Scoped to this row rather than to section 19.1, which is why the row's
        // rationale sentence was reworded at 6.3 rather than the reader widened.
        // Widening it to every fixture row would make each row enumerating three
        // or more items owe parts in the same pass, which 6.0 declined to do and
        // which this checkpoint has no evidence for either.
        var tables = ArchitectureTables.In(File.ReadAllText(Repository.Architecture));
        var table = Assert.Single(tables, candidate => candidate.Heading == Scope.FixtureTable);

        var row = Assert.Single(
            table.Body,
            candidate => candidate.Count > 1 && candidate[0] == "an inadmissible document");

        var stated = EnumeratedParts(row[1]);
        var declared = Scope.ElementsOf(CheckReach.Key(Scope.FixtureTable, "an inadmissible document"));

        // Exactly the six, in the row's own order, with nothing the rationale
        // sentence contributes. The rationale is what a comma run in it would add,
        // and this is the assertion that would report it.
        Assert.Equal(6, stated.Count);
        Assert.Equal(stated, declared);

        // And the row says six in its own words, which is the count this holds it
        // to rather than a number kept here.
        Assert.Contains("Six because", row[1], StringComparison.Ordinal);
    }

    [Fact]
    public void TheReaderTakesARowsPartsFromItsOwnWords()
    {
        // The permanent proof of the reader, over constructed text rather than
        // the document, because a reader that returned nothing would pass the
        // coverage assertion above every time.
        //
        // The list's own sentence, which is the one the fifth phase 5 sign-off
        // review found passing whole while three of its parts were undrawn.
        Assert.Equal(
            ["name", "close", "day change", "trend state in a word", "the distance row mark", "the reasons"],
            EnumeratedParts("Each row: name, close, day change, trend state in a word, the distance row mark, and the reasons"));

        // A leading connective is not part of the part, and a run of two is not
        // an enumeration: that is the reader's stated scope, and the rows that
        // carry one are decomposed by hand instead.
        Assert.Equal(
            ["the single page is here", "routing", "filters"],
            EnumeratedParts("the single page is here, with routing, filters"));
        Assert.Empty(EnumeratedParts("the plan column and the level summary for whichever row is selected"));

        // A stated rationale item is not a part. Remove it from the set and this
        // sentence yields four parts rather than three.
        Assert.Equal(
            ["every name in the index", "paged", "sorted by distance to the nearest level ascending"],
            EnumeratedParts("every name in the index, paged, sorted by distance to the nearest level ascending, so the top is what nearly fired"));
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

    // Whether a row's parts are owed a verdict yet, which is the question the
    // sweep above asks of every row in section 15.
    //
    // Any subject rather than every one, and the difference is fourteen parts.
    // A row whose subjects carry mixed verdicts, one drawn and one owed at a
    // later checkpoint, is a row whose drawn half has parts on a page now. The
    // phase 5 sign-off narrowed this to every subject and all 556 tests stayed
    // green while the reader dropped from 71 enumerated parts to 57: the
    // fourteen it stopped reading are the parts of the four rows whose subjects
    // are mixed today. Named here, and asserted over constructed verdicts, so
    // the rule is not left to a live population that happens to exercise it.
    internal static bool PartsAreOwedFor(IEnumerable<Verdict> subjects) =>
        subjects.Any(verdict => verdict == Verdict.Pass);

    [Fact]
    public void ARowIsVisitedWhereAnySubjectPassesAndNotWhereNoneDoes()
    {
        // The mixed cases are the ones narrowing the filter would lose, and they
        // are asserted both ways round so the order of a row's subjects cannot
        // decide the answer.
        Assert.True(PartsAreOwedFor([Verdict.Pass]));
        Assert.True(PartsAreOwedFor([Verdict.Pass, Verdict.OutOfScope]));
        Assert.True(PartsAreOwedFor([Verdict.OutOfScope, Verdict.Pass]));

        Assert.False(PartsAreOwedFor([Verdict.OutOfScope]));
        Assert.False(PartsAreOwedFor([Verdict.OutOfScope, Verdict.OutOfScope]));
        Assert.False(PartsAreOwedFor([]));
    }
}
