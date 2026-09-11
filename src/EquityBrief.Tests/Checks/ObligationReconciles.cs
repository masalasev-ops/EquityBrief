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
        var obligations = All();
        var checkpoints = PlanCheckpoints.All().ToDictionary(point => point.Id, point => point.Text);
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
                checkpoints.TryGetValue(candidate, out var text) && Mentions(text, obligation.Name));

            if (cited)
            {
                reconciled++;

                continue;
            }

            missing.Add(
                $"'{obligation.Name}' is owed at {string.Join(" or ", candidates)} and no such checkpoint " +
                "cites it back.");
        }

        Assert.True(
            reconciled >= RowFloor,
            $"Reconciled {reconciled} obligations against the checkpoints that owe them, expected at least {RowFloor}.");

        Assert.DoesNotContain(missing, _ => true);
    }

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
        // but one would have been a maintenance edit. The two assertions below
        // carry the property; the guard stops them passing over an empty set.
        Assert.True(open.Length >= 1, $"Found {open.Length} open checkpoint rows, so the two assertions below would pass over an empty set.");

        Assert.DoesNotContain(open, obligation => !DuePoints.InThePlan(obligation.Checkpoint!, plan));

        // A row still owed at a checkpoint the record shows as landed is a row
        // whose due point has passed with nothing saying so. A discharged row
        // is exempt, because work committed ahead of the checkpoint that owes
        // it is legitimate and its citation still has to stand.
        Assert.DoesNotContain(open, obligation => DuePoints.HasLanded(obligation.Checkpoint!, progress));
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

    // Whole phrase, whitespace tolerant and markup tolerant across the span, so
    // a name written with emphasis or wrapped across a line still matches. A
    // pattern built on a literal space is defeated by the first line break.
    internal static bool Mentions(string text, string name) =>
        Regex.IsMatch(
            text,
            string.Join(@"[\s*`_]+", name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape)),
            RegexOptions.IgnoreCase);

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
        // The forward direction, over constructed input, so the proof does not
        // depend on the corpus happening to be wrong.
        var text = "### 9.9 A checkpoint that says nothing about what it owes\nIt builds a thing.\n";

        Assert.False(Mentions(text, "A row nobody cites"));
        Assert.True(Mentions(text, "builds a thing"));

        // And the tolerance the corpus rule asks for: markup and a line break
        // across the span must not defeat the match.
        Assert.True(Mentions("it **builds**\na thing here", "builds a thing"));
    }
}
