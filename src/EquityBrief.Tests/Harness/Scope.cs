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
    const string ByHarness = "architecture-conformance";

    // Components, shared by the catalogue and the read and write matrix.
    static readonly Dictionary<string, string> Components = new(StringComparer.Ordinal)
    {
        ["Membership loader"] = "1.1",
        ["Bar fetcher"] = "1.3",
        ["Corporate action checker"] = "1.4",
        ["Indicator engine"] = "phase 2",
        ["Swing finder"] = "phase 2",
        ["Volume profile builder"] = "phase 2",
        ["Level builder"] = "phase 2",
        ["Trend classifier"] = "phase 3",
        ["Ladder builder"] = "phase 3",
        ["Move annotator"] = "phase 3",
        ["Shortlist builder"] = "phase 4",
        ["Facts assembler"] = "phase 4",
        ["Forward return filler"] = "phase 4",
        ["News pulse counter"] = "phase 4",
        ["Fundamentals fetcher"] = "phase 5",
        ["Staleness judge"] = "phase 5",
        ["Theme research runner"] = "phase 5",
        ["Research runner"] = "phase 5",
        ["Claim checker"] = "phase 5",
        ["Prose writer"] = "phase 5",
        ["Read API"] = "1.5",
        ["Single page app"] = "1.6",
        ["Report exporter"] = "phase 5",
        ["Run log"] = "1.3",
    };

    static readonly Dictionary<string, string> Stores = new(StringComparer.Ordinal)
    {
        ["Membership"] = "1.1",
        ["Bar store"] = "1.3",
        ["Indicators, swings, volume profile, levels, ladders, moves"] = "phase 2",
        ["Listings"] = "phase 4",
        ["Forward returns"] = "phase 4",
        ["Facts"] = "phase 4",
        ["Fundamentals"] = "phase 5",
        ["News pulse"] = "phase 4",
        ["Research store"] = "phase 5",
        ["Theme store"] = "phase 5",
        ["Source documents"] = "phase 5",
        ["Candidate register"] = "phase 6",
    };

    // Where a screen is complete, not where its first pixel appears. Naming a
    // later point than strictly needed says only that nothing asserts it yet,
    // which is true; naming an earlier one would be a claim that something does.
    static readonly Dictionary<string, string> Screens = new(StringComparer.Ordinal)
    {
        ["15.4 The two surfaces"] = "1.6",
        ["15.5 The mark vocabulary"] = "phase 2",
        ["15.7 Tonight"] = "phase 4",
        ["15.8 Universe"] = "phase 4",
        ["15.9 Name"] = "phase 5",
        ["15.10 Run"] = "phase 4",
        ["15.11 How a reason's record is displayed"] = "phase 6",
    };

    static readonly Dictionary<string, string> Failures = new(StringComparer.Ordinal)
    {
        ["Bulk price feed unavailable"] = "1.3",
        ["A gap in one name's series"] = "1.3",
        ["A split or dividend not caught"] = "1.4",
        ["Cloud model unavailable"] = "phase 5",
        ["A source is returned but its text cannot be retrieved"] = "phase 5",
        ["A pass finds no admissible source for a section"] = "phase 5",
        ["Claim checker rejects twice"] = "phase 5",
        ["Spend cap reached"] = "phase 5",
        ["Filing not yet parsed for a name"] = "phase 5",
        ["No band is eligible to carry a tranche"] = "phase 3",
        ["Earnings date missing"] = "phase 3",
        ["Fewer than 200 bars for a new index member"] = "phase 2",
        ["A name leaves the index"] = "1.1",
        ["A condition has fired but nothing has resolved yet"] = "phase 6",
        ["A section is assigned to the local lane that the machine cannot hold"] = "phase 5",
        ["The machine slept and the overnight queue did not run"] = "phase 5",
        ["Something tries to edit or delete a register row"] = "phase 6",
        ["The candidate register and the correction disagree"] = "phase 6",
    };

    static readonly Dictionary<string, string> NightlySteps = new(StringComparer.Ordinal)
    {
        ["Load index membership"] = "1.1",
        ["Backfill one year"] = "1.2",
        ["Fetch the day"] = "1.3",
        ["Check splits and dividends"] = "1.4",
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
        if (subject == "Migration runner")
        {
            return new Scoped(Verdict.Pass, "the schema it writes is asserted against SCHEMA.md", ByMigration);
        }

        if (subject == "Verification harness")
        {
            return new Scoped(Verdict.Pass, "this report is the thing the claim describes", ByHarness);
        }

        if (subject == "Run log" && table.StartsWith("16.", StringComparison.Ordinal))
        {
            return new Scoped(Verdict.Pass, "the table's columns and types are asserted against SCHEMA.md", ByMigration);
        }

        if (subject == "The harness cannot parse this document")
        {
            return new Scoped(Verdict.Pass, "the parse guard fails rather than reporting zero claims", ByHarness);
        }

        var due = Due(table, subject);

        return string.IsNullOrEmpty(due)
            ? throw new InvalidOperationException(
                $"No scope is declared for '{subject}' under '{table}'. A claim with no entry " +
                "would be left unexamined by omission, which is the one thing this map exists " +
                "to make impossible.")
            : new Scoped(Verdict.OutOfScope, $"nothing asserts this until {due}", string.Empty);
    }

    static string? Due(string table, string subject)
    {
        if (Screens.TryGetValue(table, out var screen))
        {
            return screen;
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

        return Stores.TryGetValue(subject, out var store) ? store : Failures.GetValueOrDefault(subject);
    }
}
