using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Checks;

internal sealed record Obligation(string Name, string CreatedAt, string DueAt, string Producer)
{
    internal bool Discharged =>
        DueAt.Contains("discharged", StringComparison.OrdinalIgnoreCase);

    // Read off the cells, never off what the row calls itself. A row that says
    // it is one form and carries the other's cells is the failure this exists
    // for.
    internal bool SaysOperating =>
        Regex.IsMatch(DueAt, @"^\s*operating\s*$", RegexOptions.IgnoreCase);

    internal string? Checkpoint =>
        Regex.Match(DueAt, @"^\s*(\d+\.\d+)").Groups[1] is { Success: true } id ? id.Value : null;

    // The trigger opens the cell. Reading it from a position rather than from
    // anywhere in the sentence is what stops the check guessing which of
    // several numbers is the one that fires: row 19's producer names 250
    // resolved setups and section 17 in the same breath.
    internal bool OpensWithATrigger =>
        Regex.IsMatch(Producer, @"^\s*\d[\d,]*\s+\S");

    internal bool NamesASurface =>
        Producer.Contains("read on the ", StringComparison.OrdinalIgnoreCase);

    internal IReadOnlyList<string> CheckpointsNamed =>
        Regex.Matches(Producer, @"(?<![\d.])\d+\.\d+(?![\d.])").Select(match => match.Value).ToArray();
}

// obligation-reconciles.
//
// BUILD_PLAN's carried obligations table had no instrument at all. Nothing
// parsed it, and a due point could name a checkpoint that produces no evidence
// for as long as nobody happened to read both the row and the checkpoint. The
// phase 2 sign-off swept 12 deferrals and found 9 of the 11 open ones naming a
// point that does not produce what they wait on, and 4 naming a point that
// produces none at all.
//
// Re-pointing those was the smaller half. This is the other half: the same
// reconciliation the due points in the architecture already have, applied to
// the table that had none.
public class ObligationReconciles
{
    // Reads is context and carries no floor. Subjects is empty on purpose: this
    // check reconciles BUILD_PLAN against itself and reaches no claim in the
    // architecture, so no placement and no verdict names it.
    internal static CheckReach Reach => new(
        "obligation-reconciles",
        ["docs/BUILD_PLAN.md", "docs/PROGRESS.md"],
        []);

    const int RowFloor = 13;

    // The section, not the document. BUILD_PLAN carries three tables whose
    // first cell is bold, and the holes table is one of them.
    internal static IReadOnlyList<Obligation> In(string plan, int floor = RowFloor)
    {
        var start = plan.IndexOf("## Carried obligations", StringComparison.Ordinal);

        var section = start < 0
            ? ""
            : plan[start..];

        var found = Regex
            .Matches(section, @"^\| \*\*(?<name>[^*]+)\*\* \| (?<created>[^|]*) \| (?<due>[^|]*) \| (?<producer>[^|]*) \|\s*$", RegexOptions.Multiline)
            .Select(match => new Obligation(
                match.Groups["name"].Value.Trim(),
                match.Groups["created"].Value.Trim(),
                match.Groups["due"].Value.Trim(),
                match.Groups["producer"].Value.Trim()))
            .ToArray();

        // A parse returning nothing would report every property as holding over
        // an empty set, which is the shape PlanCheckpoints and ArchitectureTables
        // both guard against.
        return found.Length >= floor
            ? found
            : throw new InvalidOperationException(
                $"Read {found.Length} obligations from BUILD_PLAN.md's carried obligations table, " +
                $"expected at least {floor}. A table that could not be parsed would assert every " +
                "property over nothing and report green.");
    }

    static IReadOnlyList<Obligation> All() => In(Corpus.Read("docs/BUILD_PLAN.md"));

    static IReadOnlyList<CorpusFinding> Cited() =>
        Corpus.SourceAndDocuments()
            .SelectMany(file => Corpus.Citations(Corpus.Obligation, File.ReadAllText(file), file))
            .ToArray();

    [Fact]
    public void TheTableParsesAndEveryNameIsDistinctAndUnpunctuated()
    {
        var obligations = All();

        // The rows carry the property and are floored. Files opened is context
        // and has no floor, because it is a fact about the corpus rather than
        // about this property.
        Assert.True(
            obligations.Count >= RowFloor,
            $"Found {obligations.Count} obligations, expected at least {RowFloor}.");

        Assert.Equal(
            obligations.Count,
            obligations.Select(obligation => obligation.Name).Distinct(StringComparer.Ordinal).Count());

        Assert.DoesNotContain(
            obligations,
            obligation => obligation.Name.Length > 0 && ".,;:!?".Contains(obligation.Name[^1]));
    }

    [Fact]
    public void EveryOpenRowIsExactlyOneOfTheTwoFormsReadFromItsCells()
    {
        var faults = Faults(All());

        Assert.DoesNotContain(faults, _ => true);
    }

    // Named separately from the fact so the negative proofs exercise the same
    // code the corpus is measured by.
    internal static IReadOnlyList<string> Faults(IReadOnlyList<Obligation> obligations)
    {
        var faults = new List<string>();

        foreach (var obligation in obligations.Where(row => !row.Discharged))
        {
            var checkpoint = obligation.Checkpoint;
            var trigger = obligation.OpensWithATrigger && obligation.NamesASurface;

            if (obligation.SaysOperating)
            {
                if (!obligation.OpensWithATrigger)
                {
                    faults.Add($"'{obligation.Name}' is operating and its producer does not open with a numeric trigger.");
                }

                if (!obligation.NamesASurface)
                {
                    faults.Add($"'{obligation.Name}' is operating and names no surface the trigger is read on.");
                }

                if (obligation.CheckpointsNamed.Count == 0)
                {
                    faults.Add($"'{obligation.Name}' is operating and names no checkpoint that builds its surface.");
                }

                continue;
            }

            if (checkpoint is null)
            {
                faults.Add(
                    $"'{obligation.Name}' has a due point of '{obligation.DueAt}', which is neither a " +
                    "checkpoint nor the literal operating, so it is neither form.");

                continue;
            }

            // The hybrid. It reads as tracked from either end and is chased
            // from neither, and it is the way the second form absorbs the
            // first without anything saying so.
            if (trigger)
            {
                faults.Add(
                    $"'{obligation.Name}' names checkpoint {checkpoint} and also states a trigger with a " +
                    "surface, so it carries parts of both forms and is neither.");
            }
        }

        return faults;
    }

    [Fact]
    public void EveryRowIsCitedBackByTheCheckpointThatOwesIt()
    {
        var (reconciled, missing) = CitedBack(All(), PlanCheckpoints.All());

        Assert.True(
            reconciled >= RowFloor,
            $"Reconciled {reconciled} obligations against the checkpoints that owe them, expected at least {RowFloor}.");

        Assert.DoesNotContain(missing, _ => true);
    }

    // Named separately from the fact so the proof below exercises the code the
    // corpus is measured by. A row is cited back by the marker naming it inside
    // the text of the checkpoint that owes it, and by nothing else. Its name's
    // words in that text are prose that happens to use them, which CLAUDE.md
    // gives as the reason the marker exists, and read off the words a checkpoint
    // whose heading is its row's name cited the row back with no marker at all:
    // 7.0's sweep deleted 7.1's citation of the row it owes and nothing went red.
    internal static (int Reconciled, IReadOnlyList<string> Missing) CitedBack(
        IReadOnlyList<Obligation> obligations,
        IReadOnlyList<PlanCheckpoint> points)
    {
        var checkpoints = points.ToDictionary(point => point.Id, point => point.Text);
        var missing = new List<string>();
        var reconciled = 0;

        foreach (var obligation in obligations)
        {
            // A checkpoint row is cited back by its due point. An operating row
            // is cited back by a checkpoint that builds its surface, which is
            // the only checkpoint such a row has.
            var candidates = obligation.SaysOperating
                ? obligation.CheckpointsNamed
                : obligation.Checkpoint is { } id ? [id] : Array.Empty<string>();

            var cited = candidates.Any(candidate =>
                checkpoints.TryGetValue(candidate, out var text) && Cites(text, obligation.Name));

            if (cited)
            {
                reconciled++;

                continue;
            }

            missing.Add(
                $"'{obligation.Name}' is owed at {string.Join(" or ", candidates)} and no such checkpoint " +
                "cites it back.");
        }

        return (reconciled, missing);
    }

    // The reader the other direction uses, so a citation read as resolving to a
    // row is the same citation read as citing it back.
    internal static bool Cites(string text, string name) =>
        Corpus.Citations(Corpus.Obligation, text, "docs/BUILD_PLAN.md")
            .Any(citation => string.Equals(citation.Detail, name, StringComparison.Ordinal));

    [Fact]
    public void EveryObligationCitationResolvesToARow()
    {
        var names = All().Select(obligation => obligation.Name).ToHashSet(StringComparer.Ordinal);
        var cited = Cited();

        Assert.True(cited.Count >= RowFloor, $"Found {cited.Count} obligation citations, expected at least {RowFloor}.");
        Assert.DoesNotContain(cited, citation => !names.Contains(citation.Detail));
    }

    [Fact]
    public void EveryOpenCheckpointRowNamesAPointThePlanHasAndTheRecordDoesNot()
    {
        var plan = Corpus.Read("docs/BUILD_PLAN.md");
        var progress = Corpus.Read("docs/PROGRESS.md");

        var open = All()
            .Where(obligation => !obligation.Discharged && !obligation.SaysOperating)
            .ToArray();

        // Context with a non-vacuity guard, for the reason the out-of-scope
        // counts in `architecture-conformance` became one at the phase 5
        // sign-off. The number falls as the plan is worked through, which this
        // comment said in the rule's own words for the other branch, and the
        // floor stood at 3 anyway, one below the count, so the next discharge
        // but one would have been a maintenance edit. The assertion below
        // carries both halves of the property; the guard stops it passing over
        // an empty set.
        Assert.True(open.Length >= 1, $"Found {open.Length} open checkpoint rows, so the assertion below would pass over an empty set.");

        Assert.DoesNotContain(OpenRowFaults(open, plan, progress), _ => true);
    }

    // Named separately from the fact so the proof below exercises the code the
    // corpus is measured by. The two halves are the plan lacking the row's due
    // point and the record showing it landed.
    internal static IReadOnlyList<string> OpenRowFaults(IReadOnlyList<Obligation> obligations, string plan, string progress)
    {
        var faults = new List<string>();

        foreach (var obligation in obligations.Where(row => !row.Discharged && !row.SaysOperating))
        {
            if (!DuePoints.InThePlan(obligation.Checkpoint!, plan))
            {
                faults.Add($"'{obligation.Name}' is owed at {obligation.Checkpoint}, which the plan does not have.");
            }

            // A row still owed at a checkpoint the record shows as landed is a
            // row whose due point has passed with nothing saying so. A
            // discharged row is exempt, because work committed ahead of the
            // checkpoint that owes it is legitimate and its citation still has
            // to stand.
            if (DuePoints.HasLanded(obligation.Checkpoint!, progress))
            {
                faults.Add($"'{obligation.Name}' is owed at {obligation.Checkpoint}, which the record shows as landed.");
            }
        }

        return faults;
    }

    [Fact]
    public void ARowOwedAtAPlanningCheckpointFailsOnceThatCheckpointsPlanningEntryIsRecorded()
    {
        // The shape the phase 6 sign-off found and 7.1 repaired, over a
        // constructed plan, table and record, so the proof does not wait for a
        // planning pass to arrive with a row still open against it. Until 7.1
        // the reader landed no planning checkpoint, and this row passed after
        // 9.0's planning entry exactly as it passed before it.
        var plan =
            "## Phase 9: a phase\n\n" +
            "### 9.0 Planning\nRules a thing from what is in hand (" + "owes" + ": A row owed at a planning checkpoint).\n\n" +
            "### 9.1 A checkpoint\nBuilds a thing.\n\n" +
            Table("| **A row owed at a planning checkpoint** | 8.8 | 9.0 | 9.0 rules it from what is in hand |");

        var rows = In(plan, floor: 1);

        const string before = "### 8.8 - the phase report\nBuilt:      the report.\n";
        const string planning = "\n### 9.0 planning - phase 9's detail\nNot a checkpoint entry. It belongs to 9.0.\n";
        const string ruling = "\n### 9.0 ruling - an item carried to 9.0\nNot a checkpoint entry. It belongs to 9.0, which has not landed.\n";

        Assert.Empty(OpenRowFaults(rows, plan, before));

        var fault = Assert.Single(OpenRowFaults(rows, plan, before + planning));

        Assert.Contains("which the record shows as landed", fault, StringComparison.Ordinal);

        // A ruling at the same checkpoint opens the same way and plans nothing,
        // so the row is still open after it.
        Assert.Empty(OpenRowFaults(rows, plan, before + ruling));
    }

    [Fact]
    public void EveryOperatingRowNamesACheckpointThePlanHas()
    {
        var plan = Corpus.Read("docs/BUILD_PLAN.md");

        var operating = All().Where(obligation => obligation.SaysOperating).ToArray();

        // The same shape: operating rows close as their triggers fire, so this
        // is a non-vacuity guard rather than a floor tracking the count.
        Assert.True(operating.Length >= 1, $"Found {operating.Length} operating rows, so the assertion below would pass over an empty set.");

        Assert.DoesNotContain(
            operating,
            obligation => !obligation.CheckpointsNamed.Any(point => DuePoints.InThePlan(point, plan)));
    }

    // ---- the permanent proofs that each assertion can fail ----

    const string Header =
        "## Carried obligations\n\n| Obligation | Created at | Due at | What produces the evidence |\n|---|---|---|---|\n";

    static string Table(params string[] rows) => Header + string.Join("\n", rows) + "\n";

    [Fact]
    public void AParseThatFindsNothingThrowsRatherThanReportingZero()
    {
        Assert.Throws<InvalidOperationException>(() => In("## Carried obligations\n\nnothing here\n"));
        Assert.Throws<InvalidOperationException>(() => In("a plan with no obligations section at all"));
    }

    [Fact]
    public void ARowThatIsNeitherFormFails()
    {
        var neither = In(
            Table("| **A row deferred to a phase** | 2.0 | phase 5 | somebody looks at it then |"),
            floor: 1);

        var fault = Assert.Single(Faults(neither));

        Assert.Contains("neither form", fault, StringComparison.Ordinal);
    }

    [Fact]
    public void AHybridRowCarryingBothFormsFails()
    {
        // The one that reads as tracked from either end. Without this the
        // operating form quietly absorbs the checkpoint form.
        var hybrid = In(
            Table("| **A row with a checkpoint and a trigger** | 2.0 | 5.6 | 60 nights of listings, read on the run page, which 5.6 builds |"),
            floor: 1);

        var fault = Assert.Single(Faults(hybrid));

        Assert.Contains("parts of both forms", fault, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOperatingRowWithNoTriggerOrNoSurfaceFails()
    {
        var noTrigger = In(
            Table("| **A row with no trigger** | 2.0 | operating | nights of listings, read on the run page, which 5.6 builds |"),
            floor: 1);

        var noSurface = In(
            Table("| **A row with no surface** | 2.0 | operating | 60 nights of listings, which 5.6 makes possible |"),
            floor: 1);

        Assert.Contains("does not open with a numeric trigger", Assert.Single(Faults(noTrigger)), StringComparison.Ordinal);
        Assert.Contains("names no surface", Assert.Single(Faults(noSurface)), StringComparison.Ordinal);
    }

    [Fact]
    public void AWellFormedRowOfEitherFormProducesNoFault()
    {
        // The control. A partition that refuses everything proves nothing about
        // the four proofs above.
        var both = In(
            Table(
                "| **A checkpoint row** | 2.0 | 3.6 | 3.6 widens the fixture, which is the evidence |",
                "| **An operating row** | 2.0 | operating | 250 resolved setups, read on the run page, which 5.6 builds |",
                "| **A discharged row deferred to a phase** | 2.0 | phase 5, discharged | it was done |"),
            floor: 3);

        Assert.Empty(Faults(both));
    }

    [Fact]
    public void TheReaderFindsAnObligationCitationAndAMissingNameWouldNotResolve()
    {
        var inADocument = Corpus.Citations(Corpus.Obligation, "a checkpoint (" + "owes" + ": A row nobody wrote) here", "d");
        var inCode = Corpus.Citations(Corpus.Obligation, "// " + "owes" + ": A row nobody wrote", "c");

        Assert.Equal("A row nobody wrote", Assert.Single(inADocument).Detail);
        Assert.Equal("A row nobody wrote", Assert.Single(inCode).Detail);

        Assert.DoesNotContain("A row nobody wrote", All().Select(obligation => obligation.Name));
    }

    [Fact]
    public void ACheckpointThatDoesNotCiteItsRowIsFound()
    {
        // The forward direction, over a constructed plan and table, so the proof
        // does not depend on the corpus happening to be wrong. The marker is
        // assembled, as above, so this file never cites a row that does not
        // exist.
        var marker = "(" + "owes" + ": A row its checkpoint names)";
        var row = "| **A row its checkpoint names** | 9.8 | 9.9 | 9.9 builds it |";

        Assert.Equal(0, Reconciled("### 9.9 A checkpoint that says nothing about what it owes\nIt builds a thing.\n", row));

        // The shape 7.0's sweep found: the heading is the row's name word for
        // word, and here a sentence uses it with emphasis too. Neither is a
        // citation.
        Assert.Equal(0, Reconciled("### 9.9 A row its checkpoint names\nIt builds **a row its checkpoint names**.\n", row));

        // A citation of the row in another checkpoint's text is not the
        // checkpoint that owes it citing it back, and a citation of another row
        // in its own text is not a citation of this one.
        Assert.Equal(0, Reconciled($"### 9.8 The checkpoint before\nIt names what 9.9 owes {marker}.\n\n### 9.9 A row its checkpoint names\nIt builds it.\n", row));
        Assert.Equal(0, Reconciled("### 9.9 A row its checkpoint names\nIt builds it (" + "owes" + ": Another row).\n", row));

        // The control, without which the three above prove nothing: the same
        // checkpoint with the marker in its own text.
        Assert.Equal(1, Reconciled($"### 9.9 A row its checkpoint names\nIt builds it {marker}.\n", row));
    }

    static int Reconciled(string checkpoints, string row)
    {
        var plan = checkpoints + "\n" + Table(row);
        var (reconciled, missing) = CitedBack(In(plan, floor: 1), PlanCheckpoints.In(plan, floor: 1));

        Assert.Equal(1, reconciled + missing.Count);

        return reconciled;
    }
}
