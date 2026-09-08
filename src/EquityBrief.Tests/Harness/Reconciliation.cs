using EquityBrief.Tests.Checks;

namespace EquityBrief.Tests.Harness;

// A due point, being a checkpoint like 1.3 or a phase like "phase 6".
//
// The checkpoint itself exists only once its phase is planned, which
// BUILD_PLAN does at the previous phase's sign-off, so a checkpoint is in the
// plan when the plan carries it by name or carries its phase.
// A subject the plan names in a way the derivation must not take, and the
// direction the exception claims. Later is safe and quiet; earlier is not, and
// is reported on the phase report every run.
internal sealed record DuePointException(string Subject, string Declared, bool Later);

internal static class DuePoints
{
    const string PhasePrefix = "phase ";

    // Where a due point sits in the build order, as a pair the caller can
    // compare. A phase sorts before every checkpoint in it, because "phase 5"
    // is the whole of it and 5.1 is a point inside it.
    internal static (int Phase, int Checkpoint) Order(string due)
    {
        if (NamesAPhase(due))
        {
            return (int.Parse(PhaseOf(due)), -1);
        }

        var parts = due.Split('.');

        return (int.Parse(parts[0]), parts.Length > 1 ? int.Parse(parts[1]) : -1);
    }

    internal static int Compare(string left, string right) =>
        Order(left).CompareTo(Order(right));

    internal static bool NamesAPhase(string due) =>
        due.StartsWith(PhasePrefix, StringComparison.Ordinal);

    internal static string PhaseOf(string due) =>
        NamesAPhase(due) ? due[PhasePrefix.Length..].Trim() : due.Split('.')[0];

    internal static bool InThePlan(string due, string plan) =>
        plan.Contains($"### {due} ", StringComparison.Ordinal)
        || plan.Contains($"## Phase {PhaseOf(due)}", StringComparison.Ordinal);

    internal static bool HasLanded(string due, string progress) =>
        NamesAPhase(due)
            ? progress.Contains($"### {PhaseOf(due)}.", StringComparison.Ordinal)
            : progress.Contains($"### {due} -", StringComparison.Ordinal);
}

// Reconciles what the report says an instrument covers against what that
// instrument declares it reaches, in both directions.
//
// Finding 2 and finding 3 of the phase 0 review are one defect: a placement
// names a check, and nothing compared that name against what the check reads,
// so a placement could name an instrument that never opened the file and a
// verdict could be reused for a subject the check never saw. This is the half
// that gives the declarations force.
internal static class Reconciliation
{
    // Stated in advance, because a reconciliation over zero placements passes
    // silently and would report a scope it never had. The population carrying
    // the property is the placements and passing claims that name an
    // instrument or a due point; there are ten of those today.
    // Raised from 8 to 12 at 1.1, which reconciles 14: three new passing claims
    // from component-access and the placement of section 19.1. Below the
    // measured value rather than at it, because contradiction D re-keys the
    // screens tables at 1.3 and both counts move for a correct change.
    internal const int Floor = 12;

    // A note offering the report itself as its own evidence. The report is the
    // surface a verdict is printed on and is never the instrument that reached
    // it, so this is the shape a fiat pass takes once By is populated.
    static readonly string[] SelfReferential =
    [
        "this report",
        "the report itself",
        "the report existing",
        "this document is the claim",
    ];

    internal static int Of(
        IReadOnlyList<PlacedTable> placed,
        IReadOnlyList<Claim> claims,
        IReadOnlyList<CheckReach> reaches,
        IReadOnlyList<RosterRow> roster,
        string plan,
        string progress,
        int floor = Floor)
    {
        var faults = new List<string>();
        var sent = new List<string>();
        var reconciled = 0;

        foreach (var table in placed)
        {
            if (table.Check.Length > 0)
            {
                reconciled++;
                sent.Add(table.Heading);
                faults.AddRange(Names(table.Check, table.Heading, string.Empty, reaches, roster, wholeTable: true));
            }

            if (table.Due.Length > 0)
            {
                reconciled++;
                faults.AddRange(Owes(table.Due, $"the placement of '{table.Heading}'", plan, progress));
            }

            if (table.Check.Length > 0 && table.Due.Length > 0)
            {
                faults.Add(
                    $"The placement of '{table.Heading}' names both a check and a due point. " +
                    "A table is covered by an instrument now or it is owed at a point that has " +
                    "not landed, and saying both leaves neither asserted.");
            }
        }

        foreach (var claim in claims.Where(claim => claim.Verdict == Verdict.Pass))
        {
            reconciled++;
            sent.Add(CheckReach.Key(claim.Table, claim.Subject));

            if (claim.By.Length == 0)
            {
                faults.Add(
                    $"'{claim.Subject}' under '{claim.Table}' passes and names no check. " +
                    "A PASS naming none is a PASS by fiat.");
                continue;
            }

            faults.AddRange(Names(claim.By, claim.Table, claim.Subject, reaches, roster, wholeTable: false));

            var offering = SelfReferential
                .FirstOrDefault(marker => claim.Note.Contains(marker, StringComparison.OrdinalIgnoreCase));

            if (offering is not null)
            {
                faults.Add(
                    $"'{claim.Subject}' under '{claim.Table}' passes on a note that offers the " +
                    $"report as its own evidence, on '{offering}'. A self-referential verdict is " +
                    "the fiat this guard exists to refuse.");
            }
        }

        // The other direction. A check declaring reach over something no
        // placement and no verdict sends it is a declaration nobody reads,
        // which would rot without anything noticing.
        foreach (var reach in reaches)
        {
            faults.AddRange(reach.Subjects
                .Where(subject => !sent.Contains(subject, StringComparer.Ordinal))
                .Select(subject =>
                    $"{reach.Check} declares reach over '{subject}' and nothing sends it there. " +
                    "A declaration no placement and no verdict uses is one nothing keeps current."));
        }

        if (faults.Count > 0)
        {
            throw new InvalidOperationException(
                "The report and the checks disagree about what is covered: "
                + string.Join(" ", faults));
        }

        return reconciled >= floor
            ? reconciled
            : throw new InvalidOperationException(
                $"{reconciled} placements and verdicts were reconciled against a declared reach, " +
                $"expected at least {floor}. A reconciliation over too few of them passes silently " +
                "and reports a scope it never had.");
    }

    static IEnumerable<string> Names(
        string check,
        string table,
        string subject,
        IReadOnlyList<CheckReach> reaches,
        IReadOnlyList<RosterRow> roster,
        bool wholeTable)
    {
        var where = subject.Length > 0 ? $"'{subject}' under '{table}'" : $"'{table}'";
        var row = roster.FirstOrDefault(entry => entry.Check == check);

        if (row is null)
        {
            yield return $"{where} names {check}, which is not a check CLAUDE.md's roster carries.";
            yield break;
        }

        if (row.Runs is not ("every CI run" or "the matrix"))
        {
            yield return
                $"{where} names {check}, which the roster says runs {row.Runs}. A check that does " +
                "not run yet cannot be what covers anything, and the placement owes a due point instead.";
            yield break;
        }

        var reach = reaches.FirstOrDefault(candidate => candidate.Check == check);

        if (reach is null)
        {
            yield return
                $"{where} names {check}, which declares no reach. A check named as an instrument " +
                "declares what it opens and what it can reach a verdict on.";
            yield break;
        }

        if (!reach.Covers(table, subject))
        {
            yield return
                $"{where} names {check}, whose declared reach does not include it. This is the " +
                "shape of a placement naming an instrument that never opens the file.";
        }

        // A whole table is a claim about rows the check did not enumerate, so
        // the check has to open the document those rows live in. A single row
        // may be reached by a check that asserts the code instead.
        if (wholeTable && !reach.Reads.Contains("docs/ARCHITECTURE.html", StringComparer.Ordinal))
        {
            yield return
                $"{where} is placed whole against {check}, which does not read " +
                "docs/ARCHITECTURE.html. Covering a table means covering rows nobody enumerated, " +
                "so the instrument has to open the document that carries them.";
        }
    }

    static IEnumerable<string> Owes(string due, string what, string plan, string progress)
    {
        if (!DuePoints.InThePlan(due, plan))
        {
            yield return
                $"{what} is owed at {due}, which BUILD_PLAN.md has neither as a checkpoint nor as a phase.";
        }

        if (DuePoints.HasLanded(due, progress))
        {
            yield return
                $"{what} is owed at {due}, which PROGRESS.md already records as landed. " +
                "Out of scope means a point that has not been reached.";
        }
    }
}
