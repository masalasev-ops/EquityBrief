using EquityBrief.Tests.Checks;

namespace EquityBrief.Tests.Harness;

internal enum Verdict
{
    Pass,
    Fail,
    OutOfScope,
    Unexamined,
}

// By names the check that reached the verdict. A PASS that names none, or one
// naming a check whose declared reach does not include it, is a PASS by fiat,
// which the reconciliation refuses.
internal sealed record Claim(string Table, string Subject, Verdict Verdict, string Note, string By = "");

// A table and what covers it. Check names an instrument that runs now and has
// declared it reaches this table; Due names the point at which one will.
internal sealed record PlacedTable(
    string Heading,
    int Claims,
    string Placement,
    string Check = "",
    string Due = "");

internal sealed record CheckCoverage(string Check, string Runs, string Carrier, string Reads);

internal sealed record PhaseReportModel(
    IReadOnlyList<PlacedTable> Tables,
    IReadOnlyList<Claim> Claims,
    FixtureStatus Fixture,
    IReadOnlyList<CheckCoverage> Coverage,
    int Reconciled)
{
    // The declared exceptions whose derived due point is earlier than the truth.
    // They are on the report because an unsafe derivation visible only in a
    // source comment is one nobody sees again.
    internal IReadOnlyList<DuePointException> UnsafeExceptions { get; init; } = [];

    // What the run behind this report did, carried so both surfaces and the
    // exit code read one value rather than three.
    internal SuiteRun Suite { get; init; } = SuiteRun.Nothing;

    // Carried checks that did not run or did not hold. Counted beside the
    // claims because a check can fail with no claim attached to it.
    internal int ChecksNotPassing { get; init; }

    internal int Count(Verdict verdict) => Claims.Count(claim => claim.Verdict == verdict);

    // Stated once. It was written out twice, in the command that returns the
    // exit code and in the writer that stamps the artifact, which is two
    // statements of one fact and the pair could disagree with nothing to
    // reconcile them.
    //
    // Four conditions and each covers what the others cannot. No claim was
    // checked and found wanting; none went unchecked; every carried check ran
    // and held, which catches one that reaches no claim; and the run behind it
    // was clean, which is the only one that can see a failure in a class
    // carrying no check at all.
    internal bool Green =>
        Count(Verdict.Fail) == 0
        && Count(Verdict.Unexamined) == 0
        && ChecksNotPassing == 0
        && Suite.Clean;
}

// How a table that makes no claims is placed: the reason it makes none and,
// where one exists, the instrument that covers it instead or the point at
// which one will. A placement naming neither is a table the document itself
// puts outside the claim scope, and its reason has to say so.
internal sealed record Placement(string Reason, string Check = "", string Due = "");

// Turns the architecture's tables into claims with a verdict each.
//
// Every table in the document is placed. A table that yields claims is named in
// ClaimSources; every other table is named in Placed with the reason it makes no
// claims and, where one exists, the instrument that covers it instead. A table
// in neither list stops the harness, because a table nobody placed is a table
// that can go unread.
internal static class PhaseReport
{
    // The claim scope the architecture states for itself, in the Verification
    // harness row of the component catalogue: sections 7, 14, 15, 16 and 18.
    // Section 14 carries no table, which the harness reports rather than hides.
    //
    // Section 17 is here as well, because its own note says each row is a claim
    // about the code and that the harness parses the table. It was placed as
    // asserted by pinned-constants until 0.7's review, and that check reads
    // CLAUDE.md, BUILD_PLAN.md, global.json, src/Directory.Build.props and the
    // workflow, never this document, so twenty-nine claims were removed from
    // the count by a placement naming an instrument that could not reach them.
    static readonly string[] ClaimSources =
    [
        "7. Component catalogue",
        "15.4 The two surfaces",
        "15.5 The mark vocabulary",
        "15.7 Tonight",
        "15.8 Universe",
        "15.9 Name",
        "15.10 Run",
        "15.11 How a reason's record is displayed",
        "16. Data stores and the read and write matrix",
        "Read and write matrix",
        "17. Limits, spend and the numbers the harness asserts",
        "18. Failure behaviour",
        "19.1 What a fixture holds",
    ];

    static readonly Dictionary<string, Placement> Placed = new(StringComparer.Ordinal)
    {
        ["3. Vocabulary"] = new Placement(
            "definitions the document uses, not claims it makes about the code"),
        ["4. The report, section by section, and where each part comes from"] = new Placement(
            "outside the claim scope the catalogue states; the sections it maps are asserted by the screens tables in 15"),
        ["11. The shortlist and its six reasons"] = new Placement(
            "rules, asserted by the fixture's listings expectations", Due: "4.1"),
        // Section 12.2's second table. Its heading is an h4 rather than a
        // numbered one, because the numbers in this document are navigation and
        // it sits inside 12.2 rather than beside it. The rows are the lane each
        // section is written in, which is configuration rather than structure by
        // the paragraph above them, and the shape of that configuration is the
        // hole BUILD_PLAN settles at 5.0.
        ["What each lane actually writes"] = new Placement(
            "the section-to-lane assignment, which is a setting rather than a structure and has no code behind it until the lane configuration is settled", Due: "5.0"),
        ["13.2 Four things that can improve, shallowest first"] = new Placement(
            "a plan for phase 6, with nothing built to assert it against", Due: "6.1"),
        ["13.3 The guardrails"] = new Placement(
            "rules for phase 6, asserted by register-append-only", Due: "6.1"),
        // 1.8, and the journey here is worth stating because it was wrong twice.
        //
        // It was owed at 0.6, which had landed, then re-pointed to 1.3 and then
        // to 1.1 on the reasoning that the first manifest arrives with the first
        // captured input. The manifest does arrive at 1.1 and is checked there.
        // But this table is not about the manifest: its rows are bars,
        // fundamentals, news and the expectations, and a fixture holding one
        // captured input holds almost none of what the table describes. Owing it
        // where the first manifest lands confused one row for the table.
        //
        // 1.8 is where phase 1's expectations land, which is the first point the
        // fixture holds a shape this table can be read against.
        ["19.2 What the harness checks"] = new Placement(
            "this harness's own scope, whose rows arrive with the fixture and with the components they read; the last of them is the candidate register", Due: "6.1"),
        ["19.3 What it produces"] = new Placement(
            "this harness's own output, asserted over the generated report rather than over the model behind it",
            Check: "architecture-conformance"),
        ["20. Build phases, each with its visible output"] = new Placement(
            "the plan, held by BUILD_PLAN.md and recorded against by PROGRESS.md"),
        ["Settled since the version above"] = new Placement(
            "a record of decisions taken, not a claim about code"),
        ["23. Changelog"] = new Placement(
            "a record, not a claim about code"),
    };

    // Which instrument reaches this claim, and what it would assert. This is a
    // statement about the corpus and not about any run, which is why the
    // reconciliation reads it: whether a declaration is used by a verdict is a
    // question about the map, and it has to answer the same on a run where
    // every check failed as on one where every check passed.
    static Claim Scoped(string table, string subject)
    {
        var scope = Scope.For(table, subject);

        return new Claim(table, subject, scope.Verdict, scope.Note, scope.By);
    }

    // And what that instrument actually did. Section 19.3 gives the three
    // verdicts: PASS is the claim was checked and held, FAIL is checked and did
    // not hold with the diff beside it, UNEXAMINED is not checked and never
    // counts as a pass.
    //
    // Applying this is the whole of the 0.7 repair, and applying it after the
    // reconciliation rather than before is what keeps the two questions apart.
    // Before the repair nothing applied it at all: a PASS asserted that a check
    // declared reach over the subject and never that the check had run, so
    // Verdict.Fail was reachable from nowhere and the "fail 0" line was
    // structural rather than measured.
    static Claim WithOutcome(Claim claim, SuiteOutcomes outcomes)
    {
        if (claim.Verdict != Verdict.Pass)
        {
            return claim;
        }

        var result = outcomes.For(claim.By);

        return result.Run switch
        {
            CheckRun.Passed => claim,
            CheckRun.Failed => claim with
            {
                Verdict = Verdict.Fail,
                Note = $"`{claim.By}` ran and did not hold, at {result.Test}: {result.Message}",
            },
            _ => claim with
            {
                Verdict = Verdict.Unexamined,
                Note = $"`{claim.By}` did not run in the run this report reads, so the claim was "
                    + $"not checked. It would otherwise assert that {claim.Note}",
            },
        };
    }

    internal static PhaseReportModel Build(
        IReadOnlyList<ArchitectureTable> tables,
        IReadOnlyList<string>? nightlySteps = null,
        FixtureStatus? fixture = null,
        IReadOnlyList<CheckCoverage>? coverage = null,
        SuiteOutcomes? outcomes = null)
    {
        // Absent means nothing ran, which leaves every claim a check would have
        // reached unexamined and the report not green. The default is the safe
        // direction rather than a convenience.
        outcomes ??= SuiteOutcomes.NothingRan;

        var unplaced = tables
            .Where(table => !ClaimSources.Contains(table.Heading, StringComparer.Ordinal)
                && !Placed.ContainsKey(table.Heading))
            .Select(table => table.Heading)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (unplaced.Length > 0)
        {
            throw new InvalidOperationException(
                "These tables are placed neither as claim sources nor as tables that make no " +
                "claims, so nothing would have read them: " + string.Join("; ", unplaced) +
                ". Place each one before the report can be trusted.");
        }

        var missing = ClaimSources
            .Concat(Placed.Keys)
            .Where(heading => !tables.Any(table => table.Heading == heading))
            .ToArray();

        if (missing.Length > 0)
        {
            // The parse guard. A table the harness expected and did not find
            // means the document changed or the parse broke, and reporting the
            // claims it would have carried as zero is the failure to avoid.
            throw new InvalidOperationException(
                "These tables were expected and not found: " + string.Join("; ", missing) +
                ". A missing table is a parse failure, not a table with no claims in it.");
        }

        var claims = new List<Claim>();
        var placed = new List<PlacedTable>();
        var skipped = new List<string>();

        foreach (var table in tables)
        {
            if (!ClaimSources.Contains(table.Heading, StringComparer.Ordinal))
            {
                var placement = Placed[table.Heading];

                placed.Add(new PlacedTable(
                    table.Heading, 0, placement.Reason, placement.Check, placement.Due));

                continue;
            }

            // A row is one claim unless it decomposes. Contradiction F: section
            // 15.5's Level chart names four elements drawn at three different
            // points, and one verdict over the row would hold what exists
            // hostage to what does not until phase 2.
            // A row with one cell is a heading inside the table rather than a
            // claim: section 19.1 groups its rows under Inputs, Expected
            // outputs and Expected rejections, and each of those spans the
            // table. A claim needs a subject and something said about it, so
            // one cell is the test. Counted below, because a filter that
            // quietly took real rows would look exactly like this one.
            var headings = table.Body.Count(row => row.Count == 1);

            if (headings > 0)
            {
                skipped.Add($"{table.Heading}: {headings}");
            }

            var rows = table.Body
                .Where(row => row.Count > 1 && row[0].Length > 0)
                .SelectMany(row => Scope.SubjectsOf(table.Heading, row[0]))
                .Select(subject => Scoped(table.Heading, subject))
                .ToArray();

            claims.AddRange(rows);
            placed.Add(new PlacedTable(table.Heading, rows.Length, "claim source"));
        }

        // Section 14 is named as a claim source and carries an ordered list
        // rather than a table, so its steps are read as claims too.
        var steps = (nightlySteps ?? [])
            .Select(step => Scoped(NightlyRunSteps.Heading, step))
            .ToArray();

        if (steps.Length > 0)
        {
            claims.AddRange(steps);
            placed.Add(new PlacedTable(NightlyRunSteps.Heading, steps.Length, "claim source, read as a list"));
        }

        // Both directions, and it stops the harness on either. A placement or a
        // verdict naming an instrument that cannot reach it is what made green
        // mean less than it claimed.
        var reconciled = Reconciliation.Of(
            placed,
            claims,
            CheckReaches.All(),
            CoverageReported.Roster(),
            Corpus.Read("docs/BUILD_PLAN.md"),
            Corpus.Read("docs/PROGRESS.md"));

        // Stated rather than silent. Three headings in one table today; a
        // fourth appearing somewhere else is a table shape nobody looked at.
        if (skipped.Count > 1)
        {
            throw new InvalidOperationException(
                "More than one table carries rows with a single cell, which this reads as headings " +
                "inside a table: " + string.Join("; ", skipped) +
                ". One table doing that is a shape the harness knows; two is a shape nobody has read.");
        }

        // The outcomes are applied here, after the reconciliation has read the
        // claims as the corpus states them. A claim whose check failed or did
        // not run keeps the instrument it names, because which instrument
        // reaches it does not change with the run.
        return new PhaseReportModel(
            placed,
            [.. claims.Select(claim => WithOutcome(claim, outcomes))],
            fixture ?? new FixtureStatus(0, "ABSENT", "not looked for", []),
            coverage ?? [],
            reconciled)
        {
            UnsafeExceptions = [.. Scope.Exceptions().Where(exception => !exception.Later)],
            Suite = outcomes.Run,
            ChecksNotPassing = outcomes.Count(CheckRun.Failed) + outcomes.Count(CheckRun.DidNotRun),
        };
    }
}
