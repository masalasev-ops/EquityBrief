namespace EquityBrief.Tests.Harness;

internal enum Verdict
{
    Pass,
    Fail,
    OutOfScope,
    Unexamined,
}

// By names the check that reached the verdict. A PASS that names none is a
// PASS by fiat, which architecture-conformance refuses.
internal sealed record Claim(string Table, string Subject, Verdict Verdict, string Note, string By = "");

internal sealed record PlacedTable(string Heading, int Claims, string Placement);

internal sealed record PhaseReportModel(
    IReadOnlyList<PlacedTable> Tables,
    IReadOnlyList<Claim> Claims)
{
    internal int Count(Verdict verdict) => Claims.Count(claim => claim.Verdict == verdict);
}

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
        "18. Failure behaviour",
    ];

    static readonly Dictionary<string, string> Placed = new(StringComparer.Ordinal)
    {
        ["3. Vocabulary"] =
            "definitions the document uses, not claims it makes about the code",
        ["4. The report, section by section, and where each part comes from"] =
            "outside the claim scope the catalogue states; the sections it maps are asserted by the screens tables in 15",
        ["11. The shortlist and its six reasons"] =
            "rules, asserted by the fixture's listings expectations from 4.1",
        ["13.2 Four things that can improve, shallowest first"] =
            "a plan for phase 6, with nothing built to assert it against",
        ["13.3 The guardrails"] =
            "rules for phase 6, asserted by register-append-only from 6.1",
        ["17. Limits, spend and the numbers the harness asserts"] =
            "constants, asserted by pinned-constants rather than enumerated here, so one fact keeps one instrument",
        ["19.1 What a fixture holds"] =
            "the fixture's shape, asserted by the fixture manifest from 0.6",
        ["19.2 What the harness checks"] =
            "this harness's own scope, asserted by coverage-reported",
        ["19.3 What it produces"] =
            "this harness's own output, asserted by the report existing and being read",
        ["20. Build phases, each with its visible output"] =
            "the plan, held by BUILD_PLAN.md and recorded against by PROGRESS.md",
        ["Settled since the version above"] =
            "a record of decisions taken, not a claim about code",
        ["23. Changelog"] =
            "a record, not a claim about code",
    };

    internal static PhaseReportModel Build(IReadOnlyList<ArchitectureTable> tables)
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
                placed.Add(new PlacedTable(table.Heading, 0, Placed[table.Heading]));
                continue;
            }

            var rows = table.Body
                .Where(row => row.Count > 0 && row[0].Length > 0)
                .Select(row => new Claim(
                    table.Heading,
                    row[0],
                    Verdict.Unexamined,
                    "no check asserts this yet"))
                .ToArray();

            claims.AddRange(rows);
            placed.Add(new PlacedTable(table.Heading, rows.Length, "claim source"));
        }

        return new PhaseReportModel(placed, claims);
    }
}
