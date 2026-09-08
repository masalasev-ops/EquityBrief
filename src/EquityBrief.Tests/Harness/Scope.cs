namespace EquityBrief.Tests.Harness;

internal sealed record Scoped(Verdict Verdict, string Note, string By);

// Where every claim in the architecture is answered.
//
// A claim is PASS only when a named check reaches it. Everything else names the
// checkpoint or phase that ends it, which is what out of scope means: the corpus
// places it at a point that has not landed. Nothing is left unexamined by
// omission, because a subject with no entry here stops the harness.
internal static class Scope
{
    const string ByMigration = "schema-columns";
    const string ByAccess = "component-access";
    const string ByHarness = "architecture-conformance";

    internal const string MatrixTable = "Read and write matrix";
    internal const string CatalogueTable = "7. Component catalogue";
    internal const string StoresTable = "16. Data stores and the read and write matrix";
    internal const string FailureTable = "18. Failure behaviour";
    internal const string LimitsTable = "17. Limits, spend and the numbers the harness asserts";

    // The ones a check has actually reached, keyed on the table and the subject
    // together. Keyed on the subject alone until 0.7's review, which meant a
    // catalogue verdict was reused verbatim for the row of the same name in the
    // read and write matrix, where the claim is a different one.
    static readonly Dictionary<string, Scoped> Reached = new(StringComparer.Ordinal)
    {
        [CheckReach.Key(CatalogueTable, "Migration runner")] = new Scoped(
            Verdict.Pass,
            "the schema it writes is asserted against SCHEMA.md, column by column and type by type",
            ByMigration),
        [CheckReach.Key(CatalogueTable, "Verification harness")] = new Scoped(
            Verdict.Pass,
            "every claim in sections 7, 14, 15, 16, 17 and 18 carries a verdict, and both artifacts are written and read back",
            ByHarness),
        [CheckReach.Key(CatalogueTable, "Backfill")] = new Scoped(
            Verdict.Pass,
            "the class declares the feed it reads and the stores it touches, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Backfill")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included",
            ByAccess),
        [CheckReach.Key(StoresTable, "Bar store")] = new Scoped(
            Verdict.Pass,
            "the table's columns and types are asserted against SCHEMA.md",
            ByMigration),
        [CheckReach.Key(StoresTable, "Membership")] = new Scoped(
            Verdict.Pass,
            "the table's columns and types are asserted against SCHEMA.md",
            ByMigration),
        [CheckReach.Key(StoresTable, "Run log")] = new Scoped(
            Verdict.Pass,
            "the table's columns and types are asserted against SCHEMA.md",
            ByMigration),
        [CheckReach.Key(CatalogueTable, "Membership loader")] = new Scoped(
            Verdict.Pass,
            "the class declares the feed it reads and the stores it touches, and the declaration matches this row, its matrix row, SCHEMA's ownership and the statements in its own source",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Membership loader")] = new Scoped(
            Verdict.Pass,
            "every cell of the row is asserted against the declaration, the blanks included",
            ByAccess),
        [CheckReach.Key(MatrixTable, "Migration runner")] = new Scoped(
            Verdict.Pass,
            "the row is eleven blanks, the runner declares no store, and no statement against a declared table appears in its source",
            ByAccess),
        [CheckReach.Key(FailureTable, "The harness cannot parse this document")] = new Scoped(
            Verdict.Pass,
            "the parse guard fails rather than reporting zero claims",
            ByHarness),
    };

    // Where the plan names a subject, the due point is read from the plan and
    // is not written here. What follows is the residue: subjects BUILD_PLAN's
    // checkpoint text does not name, and two it names in a way that cannot be
    // read. Every entry below is one the derivation could not supply, and the
    // reconciliation asserts that in both directions, so an entry that becomes
    // derivable later fails rather than silently shadowing the plan.

    // The plan names these and the derivation must not take them. They are kept
    // in two lists rather than one, because the two directions are not the same
    // kind of thing and treating them alike hides the dangerous one.
    //
    // Each entry says why. An exception with no reason is the mechanism by which
    // a derivation gets quietly switched off, and the direction each list
    // declares is asserted against the plan rather than trusted, because a
    // mislabelled exception is the one failure the split cannot otherwise catch.

    // Derived later than the truth. A late due point can only delay a claim: it
    // says nothing asserts something that already does, which is a smaller
    // statement than the truth and never a failing one. Declared, and quiet.
    static readonly Dictionary<string, string> DerivedIsLate = new(StringComparer.Ordinal)
    {
        // 4.5 creates the table and the plan writes `forward_return` there in
        // the snake case the schema uses, so the plural store name matches
        // nothing until 6.1 mentions forward returns in prose.
        ["Forward returns"] = "4.5",
    };

    // Derived earlier than the truth. An early due point fails the day the
    // checkpoint it names lands, because out of scope means a point that has
    // not been reached and the reconciliation refuses one that has.
    //
    // These report themselves on the phase report every run. A known unsafe
    // derivation sitting in the tree and visible only in a source comment is an
    // exception nobody sees again, and an exception nobody sees again becomes
    // one nobody remembers.
    static readonly Dictionary<string, string> DerivedIsEarly = new(StringComparer.Ordinal)
    {
        // 1.7 produces the first draft of the source lists, which is why it
        // names them. The limits row is not about the lists existing: it is
        // about a search returning only sites on the list that applies to it,
        // and the row's own Asserted by column names a fixture search. That
        // arrives with the research pass at 5.1.
        ["Source lists"] = "5.1",

        // 1.2 names the run log, because that is where the backfill's request
        // count first reaches it. The catalogue row is not about one stage: its
        // Reads cell says "every component appends", so the row is a claim about
        // every component, and the last of them lands in phase 6.
        ["Run log"] = "phase 6",

        // 1.2 builds the backfill, and this row is the limit on it rather than
        // the component. Its own Asserted by column names the run log's request
        // count against the names lacking history, which is nightly-cost reading
        // a recorded run, and that arrives at 1.4.
        ["Backfill"] = "1.4",
    };

    // Components the plan does not name. The catalogue and the matrix share it.
    static readonly Dictionary<string, string> Components = new(StringComparer.Ordinal)
    {
        // 3.2 builds the ladder and never uses the component's name.
        ["Ladder builder"] = "3.2",
        ["Single page app"] = "1.6",
        ["Report exporter"] = "phase 5",
    };

    static readonly Dictionary<string, string> Stores = new(StringComparer.Ordinal)
    {
        ["Indicators, swings, volume profile, levels, ladders, moves"] = "phase 2",
        ["Research store"] = "phase 5",
        ["Theme store"] = "phase 5",
        ["Source documents"] = "phase 5",
    };

    // Where a screen is complete, not where its first pixel appears. Naming a
    // later point than strictly needed says only that nothing asserts it yet,
    // which is true; naming an earlier one would be a claim that something does.
    static readonly Dictionary<string, string> Screens = new(StringComparer.Ordinal)
    {
        ["15.4 The two surfaces"] = "1.3",
        ["15.5 The mark vocabulary"] = "phase 2",
        ["15.7 Tonight"] = "phase 4",
        ["15.8 Universe"] = "phase 4",
        ["15.9 Name"] = "phase 5",
        ["15.10 Run"] = "phase 4",
        ["15.11 How a reason's record is displayed"] = "phase 6",
    };

    static readonly Dictionary<string, string> Failures = new(StringComparer.Ordinal)
    {
        ["Bulk price feed unavailable"] = "1.4",
        ["A gap in one name's series"] = "1.5",
        ["A split or dividend not caught"] = "1.6",
        ["Cloud model unavailable"] = "phase 5",
        ["A source is returned but its text cannot be retrieved"] = "phase 5",
        ["A pass finds no admissible source for a section"] = "phase 5",
        ["Claim checker rejects twice"] = "phase 5",
        ["Spend cap reached"] = "phase 5",
        ["Filing not yet parsed for a name"] = "phase 5",
        ["No band is eligible to carry a tranche"] = "phase 3",
        ["Earnings date missing"] = "phase 3",
        ["Fewer than 200 bars for a new index member"] = "phase 2",
        // The row's "What you see" cell claims the name disappears from the
        // universe screen, which is 4.1. 1.1 records the leave date and asserts
        // nothing a reader looks at.
        ["A name leaves the index"] = "4.1",
        ["A condition has fired but nothing has resolved yet"] = "phase 6",
        ["A section is assigned to the local lane that the machine cannot hold"] = "phase 5",
        ["The machine slept and the overnight queue did not run"] = "phase 5",
        ["Something tries to edit or delete a register row"] = "phase 6",
        ["The candidate register and the correction disagree"] = "phase 6",
    };

    // The two rows of the read and write matrix whose component already exists.
    // Every other row resolves through Components, which the catalogue shares.
    // A matrix row claims what its component touches across eleven stores, and
    // eight of those stores are not built, so the row is not assertable until
    // the last of them is.
    static readonly Dictionary<string, string> MatrixRows = new(StringComparer.Ordinal)
    {
        ["Verification harness"] = "phase 6",
    };

    // Section 17's limits. Each row is a claim about the code, and the code
    // that would carry it arrives with the component the row constrains.
    static readonly Dictionary<string, string> LimitDuePoints = new(StringComparer.Ordinal)
    {
        // nightly-cost is implemented at 1.4, over the shipped source and a
        // recorded run, which is the first point either limit is asserted.
        ["Model calls in the nightly run"] = "1.4",
        ["Per-name network calls in the nightly run"] = "1.4",
        ["Nightly wall clock, 500 names"] = "phase 4",
        // Retention is what makes the year a limit rather than a description,
        // and it lands with the fetcher at 1.4.
        ["Bar history kept"] = "1.4",
        ["Level window"] = "phase 2",
        ["Swing lookback"] = "phase 2",
        ["Band merge distance"] = "phase 2",
        ["Tranches, exits"] = "phase 3",
        ["Tranche eligibility"] = "phase 3",
        ["Earnings horizon"] = "phase 3",
        ["List display"] = "phase 4",
        ["Research passes per name per open"] = "phase 5",
        ["Research staleness triggers"] = "phase 5",
        ["Scheduling of queued work"] = "phase 5",
        ["Claim rejection"] = "5.1",
        ["Theme search parameters"] = "phase 5",
        ["Source admissibility"] = "5.1",
        ["Nightly row coverage"] = "4.1",
        ["Reason record display"] = "phase 6",
        ["Minimum resolved setups"] = "phase 6",
        ["Family size and correction"] = "6.1",
        ["Frozen measurement windows"] = "phase 6",
    };

    static readonly Dictionary<string, string> NightlySteps = new(StringComparer.Ordinal)
    {
        // The step is a claim about the nightly script running it in order, and
        // the script is built at 1.4. A step whose component lands earlier is
        // still not run by a night until then.
        ["Load index membership"] = "1.4",
        ["Backfill one year"] = "1.4",
        ["Fetch the day"] = "1.4",
        ["Check splits and dividends"] = "1.6",
        ["For every name"] = "phase 4",
        ["Fill forward returns"] = "phase 4",
        ["Count today"] = "phase 4",
        ["Close the arithmetic"] = "phase 4",
        ["Run the overnight queue"] = "phase 5",
    };

    internal static Scoped For(string table, string subject)
    {
        // The ones that have landed. Each names the check that reached it,
        // because a PASS naming none is a PASS by fiat.
        if (Reached.TryGetValue(CheckReach.Key(table, subject), out var reached))
        {
            return reached;
        }

        var due = Due(table, subject);

        return string.IsNullOrEmpty(due)
            ? throw new InvalidOperationException(
                $"No scope is declared for '{subject}' under '{table}'. A claim with no entry " +
                "would be left unexamined by omission, which is the one thing this map exists " +
                "to make impossible.")
            : new Scoped(Verdict.OutOfScope, $"nothing asserts this until {due}", string.Empty);
    }

    // Every subject and the due point it resolves to, so the reconciliation can
    // read the two halves apart: what the plan supplied, and what is written
    // here because the plan could not.
    internal static IReadOnlyList<string> ResidualSubjects() =>
        [.. DerivedIsLate.Keys, .. DerivedIsEarly.Keys, .. Components.Keys, .. Stores.Keys,
            .. Failures.Keys, .. MatrixRows.Keys, .. LimitDuePoints.Keys, .. NightlySteps.Keys];

    internal static IReadOnlyList<string> DeclaredExceptions() =>
        [.. DerivedIsLate.Keys, .. DerivedIsEarly.Keys];

    // Late first, then early, each with the direction it claims, so the
    // reconciliation can assert the claim rather than take the label.
    internal static IReadOnlyList<DuePointException> Exceptions() =>
    [
        .. DerivedIsLate.Select(pair => new DuePointException(pair.Key, pair.Value, Later: true)),
        .. DerivedIsEarly.Select(pair => new DuePointException(pair.Key, pair.Value, Later: false)),
    ];

    static string? Due(string table, string subject)
    {
        // The ones the plan names in a way that cannot be read, each carrying
        // the reason beside it above.
        if (DerivedIsLate.TryGetValue(subject, out var late))
        {
            return late;
        }

        if (DerivedIsEarly.TryGetValue(subject, out var early))
        {
            return early;
        }

        if (Screens.TryGetValue(table, out var screen))
        {
            return screen;
        }

        if (table == LimitsTable && LimitDuePoints.TryGetValue(subject, out var limit))
        {
            return limit;
        }

        if (table == MatrixTable && MatrixRows.TryGetValue(subject, out var row))
        {
            return row;
        }

        if (table == NightlyRunSteps.Heading)
        {
            return NightlySteps
                .FirstOrDefault(step => subject.StartsWith(step.Key, StringComparison.Ordinal))
                .Value;
        }

        if (Components.TryGetValue(subject, out var component))
        {
            return component;
        }

        if (Stores.TryGetValue(subject, out var store))
        {
            return store;
        }

        if (Failures.TryGetValue(subject, out var failure))
        {
            return failure;
        }

        // Nothing above it carried this subject, so the plan is asked. This is
        // the half that cannot go stale: BUILD_PLAN's checkpoint text is the
        // first statement of which checkpoint does the work, and a due point
        // read from it moves when the plan is reordered.
        return PlanCheckpoints.DueFor(subject);
    }
}
