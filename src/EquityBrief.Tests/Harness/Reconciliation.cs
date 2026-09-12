using System.Text.RegularExpressions;
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

    // Whether the plan has the point a due point names.
    //
    // The checkpoint by name where the phase has been detailed, and the phase
    // alone where it has not. The fallback exists because a later phase gets its
    // checkpoints only at the previous phase's sign-off, so requiring a
    // checkpoint by name would forbid the roster from naming anything past the
    // phase in hand, which is the case `CLAUDE.md` argues for. It applied to
    // every due point until the sixth phase 5 sign-off review, so "5.8" was in
    // the plan before 5.8 was written and an obligation could be owed at a
    // checkpoint nobody had created.
    // see: A due point names a checkpoint that exists wherever its phase has been detailed
    internal static bool InThePlan(string due, string plan) =>
        plan.Contains($"### {due} ", StringComparison.Ordinal)
        || (!Detailed(PhaseOf(due), plan)
            && plan.Contains($"## Phase {PhaseOf(due)}", StringComparison.Ordinal));

    // Whether a phase carries checkpoint detail: any heading of its own inside
    // it. Read from the plan rather than from a list of phases kept beside this,
    // which would be a second statement of the same fact.
    static bool Detailed(string phase, string plan) =>
        plan.Contains($"\n### {phase}.", StringComparison.Ordinal);

    // The checkpoints PROGRESS records as built, read from its entries rather
    // than matched against its text.
    //
    // The text match this replaced asked whether the file contained "### 2.",
    // which is a question about everything sharing that prefix. An entry headed
    // "### 2.0 planning" answered it, so the pass that plans phase 2 would have
    // read as phase 2 having landed and failed every claim still owed at it:
    // the mark vocabulary rows, the level window, the swing lookback, the band
    // merge distance. "### 1.1 planning" already answered it for phase 1, and
    // nothing noticed only because nothing is due at bare "phase 1", which is
    // the state that carries a defect past the point where it bites.
    //
    // A planning pass is told from a checkpoint by the convention CLAUDE.md
    // already states, the entry opening "Not a checkpoint entry", rather than
    // by its number. The number is what misled the matcher, and an entry headed
    // "### 2.0 -" whose body opens that way is still a planning pass.
    internal static IReadOnlyList<string> Built(string progress)
    {
        var built = new List<string>();

        // The heading is the rest of its own line and the body runs to the next
        // one, so the character class is spelled out rather than left to a dot.
        // A dot that matches newlines makes the heading swallow the file and
        // the reader returns nothing, which is a parse failure that reads as a
        // record with no checkpoints in it.
        foreach (Match entry in Regex.Matches(
                     progress,
                     @"^### (?<heading>[^\r\n]*)(?<body>(?:(?!^### )[\s\S])*)",
                     RegexOptions.Multiline))
        {
            var id = Regex.Match(entry.Groups["heading"].Value, @"^(\d+\.\d+)");

            if (!id.Success || entry.Groups["body"].Value.TrimStart()
                    .StartsWith(NotACheckpoint, StringComparison.Ordinal))
            {
                continue;
            }

            built.Add(id.Groups[1].Value);
        }

        return built;
    }

    internal const string NotACheckpoint = "Not a checkpoint entry";

    internal static bool HasLanded(string due, string progress) =>
        HasLanded(due, Built(progress));

    internal static bool HasLanded(string due, IReadOnlyList<string> built) =>
        NamesAPhase(due)
            ? built.Any(checkpoint => PhaseOf(checkpoint) == PhaseOf(due))
            : built.Contains(due, StringComparer.Ordinal);
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
    // Raised from 8 to 12 at 1.1, and from 12 to 20 at 1.3, which reconciles
    // 27: nine new passing claims, six from component-access over the read API,
    // the mark renderer and the app, and three from read-surface over the
    // screens rows. Below the measured value rather than at it, and stated
    // before the run: 18 passing claims were predicted and 20 measured, the two
    // extra being the app's catalogue and matrix rows, whose due point was a
    // stale 1.6 left over from the ordering 8ac2442 replaced.
    //
    // Raised from 20 to 28 at 1.4, which reconciles 36. Predicted before the
    // run: 29 passing claims, being 1.3's 20 plus nine of the ten owed at 1.4,
    // the tenth being the bulk-feed failure row whose due point moved to 5.4
    // because its "What you see" cell promises a banner and tonight's list.
    // Measured 29.
    // Raised from 28 to 34 at 1.6, which reconciles 42. Predicted before the
    // run: 35 passing claims, being 1.5's 30 plus the five owed at 1.6, one of
    // which is a store row this checkpoint adds. Measured 35.
    internal const int Floor = 34;

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
