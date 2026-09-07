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
    internal int Count(Verdict verdict) => Claims.Count(claim => claim.Verdict == verdict);
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
    ];

    static readonly Dictionary<string, Placement> Placed = new(StringComparer.Ordinal)
    {
        ["3. Vocabulary"] = new Placement(
            "definitions the document uses, not claims it makes about the code"),
        ["4. The report, section by section, and where each part comes from"] = new Placement(
            "outside the claim scope the catalogue states; the sections it maps are asserted by the screens tables in 15"),
        ["11. The shortlist and its six reasons"] = new Placement(
            "rules, asserted by the fixture's listings expectations", Due: "4.1"),
        ["13.2 Four things that can improve, shallowest first"] = new Placement(
            "a plan for phase 6, with nothing built to assert it against", Due: "6.1"),
        ["13.3 The guardrails"] = new Placement(
            "rules for phase 6, asserted by register-append-only", Due: "6.1"),
        ["19.1 What a fixture holds"] = new Placement(
            "the fixture's shape, asserted by the manifest checker over the first fixture that lands", Due: "1.3"),
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

    static Claim Scoped(string table, string subject)
    {
        var scope = Scope.For(table, subject);

        return new Claim(table, subject, scope.Verdict, scope.Note, scope.By);
    }

    internal static PhaseReportModel Build(
        IReadOnlyList<ArchitectureTable> tables,
        IReadOnlyList<string>? nightlySteps = null,
        FixtureStatus? fixture = null,
        IReadOnlyList<CheckCoverage>? coverage = null)
    {
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

        foreach (var table in tables)
        {
            if (!ClaimSources.Contains(table.Heading, StringComparer.Ordinal))
            {
                var placement = Placed[table.Heading];

                placed.Add(new PlacedTable(
                    table.Heading, 0, placement.Reason, placement.Check, placement.Due));

                continue;
            }

            var rows = table.Body
                .Where(row => row.Count > 0 && row[0].Length > 0)
                .Select(row => Scoped(table.Heading, row[0]))
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

        return new PhaseReportModel(
            placed,
            claims,
            fixture ?? new FixtureStatus(0, "ABSENT", "not looked for"),
            coverage ?? [],
            reconciled);
    }
}
