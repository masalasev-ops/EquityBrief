using System.Text.RegularExpressions;

namespace EquityBrief.Tests.Checks;

// done-condition-producible. No done condition in a spec waits on evidence that
// only the calendar produces.
//
// The rule exists in CLAUDE.md and arrived at 5.7 with nothing asserting it.
// 5.7 found its own done condition reading as a week of unattended nights,
// amended it, and swept one of the four places the corpus stated it, so section
// 20 went on saying the phase was done when a week of nights had run. The phase
// 5 sign-off read that cell and reported the phase as not done. An amendment
// that lands in one of two documents holding one fact leaves the other as the
// one a reader believes.
//
// So the property is not "the wording was fixed" but "no done condition can say
// this again", which is the only form that survives the next planning pass.
public class DoneConditionProducible
{
    // What accumulates rather than being produced. Each pattern needs a
    // quantity of two or more, because one night, one session and one run are
    // all things a checkpoint produces from inside a session, and it is the
    // waiting for several that stops the build.
    //
    // A period noun carries its own waiting: "a week of" and "three months of"
    // are spans of calendar whatever follows them. A span of history is not,
    // which is why `year` is absent: phase 1 is done when every member holds a
    // full year of bars, and that year arrives in one backfill rather than by
    // anybody waiting for it.
    static readonly (string Name, Regex Pattern)[] Accumulations =
    [
        // The article forms are here because 5.1 said "the week of nights that
        // produces a distribution" rather than "a week of nights", and a
        // matcher keyed on the quantity alone would have read the second and
        // missed the first while reporting the same green.
        ("a span of calendar",
            new Regex(@"\b(a|an|the|this|that|one|two|three|four|five|six|seven|eight|nine|ten|\d+|several|many)\s+(week|weeks|fortnight|month|months)\b",
                RegexOptions.IgnoreCase)),

        ("a span of calendar, unquantified",
            new Regex(@"\b(weeks|months)\s+of\b", RegexOptions.IgnoreCase)),

        ("several nights, days or sessions",
            new Regex(@"\b(two|three|four|five|six|seven|eight|nine|ten|\d+|several|many)\s+(\w+\s+)?(nights|days|sessions|evenings|mornings)\b",
                RegexOptions.IgnoreCase)),

        ("a run nobody attends",
            new Regex(@"\bunattended\b", RegexOptions.IgnoreCase)),

        ("evidence stated as accumulating",
            new Regex(@"\baccumulat\w*\b", RegexOptions.IgnoreCase)),
    ];

    // The two places a done condition is written. Nowhere else states one, and
    // scanning wider would read the rule that forbids this as a use of it:
    // CLAUDE.md's own convention has to contain the words "a week of unattended
    // nights" in order to name what it refuses, exactly as banned-prose has to
    // exempt the sentence naming its string.
    internal static IReadOnlyList<CorpusFinding> Findings()
    {
        var found = new List<CorpusFinding>();

        foreach (var condition in PlanConditions().Concat(PhaseTableConditions()))
        {
            foreach (var (name, pattern) in Accumulations)
            {
                var hit = pattern.Match(condition.Detail);

                if (hit.Success)
                {
                    found.Add(condition with
                    {
                        Detail = $"{condition.Detail[..Math.Min(condition.Detail.Length, 90)]} ... waits on {name}: '{hit.Value}'",
                    });

                    break;
                }
            }
        }

        return found;
    }

    // Every checkpoint's done condition in BUILD_PLAN, taken as the whole
    // paragraph rather than the first sentence: a done condition legitimately
    // runs to two or three, and several say why the second one is separate.
    // Taken as the paragraph, a clause sitting in it is part of it, which is
    // what 5.1 was repaired for.
    internal static IReadOnlyList<CorpusFinding> PlanConditions() =>
        Conditions(Corpus.Read("docs/BUILD_PLAN.md"), "docs/BUILD_PLAN.md", @"^\*\*Done when\*\*.*$");

    static IReadOnlyList<CorpusFinding> Conditions(string text, string file, string opening)
    {
        var lines = text.Split((char)10);
        var found = new List<CorpusFinding>();

        for (var index = 0; index < lines.Length; index++)
        {
            if (Regex.IsMatch(lines[index], opening))
            {
                found.Add(new CorpusFinding(file, index + 1, lines[index]));
            }
        }

        return found;
    }

    // Section 20's Done when column, which is the same fact stated per phase.
    // The column is last in each row, and the rows are the phases.
    internal static IReadOnlyList<CorpusFinding> PhaseTableConditions()
    {
        var architecture = Corpus.Read("docs/ARCHITECTURE.html");
        var section = architecture.IndexOf("<h2>20. Build phases", StringComparison.Ordinal);

        Assert.True(section > 0, "ARCHITECTURE.html has no section 20 to read the phase table from.");

        var end = architecture.IndexOf("<h2>21.", section, StringComparison.Ordinal);
        var table = architecture[section..(end > 0 ? end : architecture.Length)];
        var line = architecture[..section].Count(character => character == (char)10) + 1;

        // Which column is read, asserted from the document's own header rather
        // than from the reader's memory of the column order. Without this the
        // check reads whichever cell happens to be last, so a column added to
        // the right of Done when would leave it scanning the new column and
        // passing over every done condition in the table. That is the shape
        // this corpus keeps finding: an instrument that narrows its own scope
        // and stays green.
        var headers = Regex.Matches(table, @"<th[^>]*>(.*?)</th>", RegexOptions.Singleline)
            .Select(header => header.Groups[1].Value.Trim())
            .ToArray();

        Assert.Equal(5, headers.Length);
        Assert.Equal("Done when", headers[^1]);

        var found = new List<CorpusFinding>();

        foreach (Match row in Regex.Matches(table, @"<tr><td><b>(\d+)\.[^<]*</b></td>(.*?)</tr>", RegexOptions.Singleline))
        {
            var cells = Regex.Matches(row.Groups[2].Value, @"<td>(.*?)</td>", RegexOptions.Singleline)
                .Select(cell => cell.Groups[1].Value)
                .ToArray();

            Assert.True(
                cells.Length == 4,
                $"Phase {row.Groups[1].Value}'s row in section 20 has {cells.Length} cells after the phase, expected 4.");

            found.Add(new CorpusFinding(
                "docs/ARCHITECTURE.html",
                line + architecture[section..(section + row.Index)].Count(character => character == (char)10),
                $"phase {row.Groups[1].Value}: {cells[^1]}"));
        }

        return found;
    }

    [Fact]
    public void NoDoneConditionWaitsOnTheCalendar()
    {
        var plan = PlanConditions();
        var phases = PhaseTableConditions();

        // The scope carrying the property is the done conditions themselves, so
        // the floor sits on them and not on the files opened. 55 was the count
        // at 5.7, one per checkpoint across all eight phases, and it only rises
        // as checkpoints are added. 8 is exact because section 20 has one row
        // per phase and the phases are fixed at eight.
        Assert.True(plan.Count >= 55, $"Read {plan.Count} done conditions in BUILD_PLAN, expected at least 55.");
        Assert.Equal(8, phases.Count);

        var findings = Findings();

        Assert.True(
            findings.Count == 0,
            "A done condition may not require calendar time:" + (char)10 +
            string.Join((char)10, findings.Select(finding => $"  {finding.File}:{finding.Line}  {finding.Detail}")));
    }

    [Fact]
    public void TheGuardCatchesTheConditionItWasWrittenFor()
    {
        // The permanent proof that the assertion above can fail. A sweep whose
        // expected result is nothing is self-validating: a matcher that matches
        // nothing at all passes it every time. So the text 5.7 removed, and the
        // text section 20 carried until this repair, are both run through the
        // same matcher and asserted to be caught.
        var caught = new[]
        {
            "a week of unattended nights with the list current each morning, and every stored listing carrying its entry, stop and target",
            "**Done when** a week of unattended nights has run and the posting hour is recorded from those nights",
            "**Done when** five scheduled nights over the whole index have run",
            "**Done when** three months of resolved setups have accumulated",
            "**Done when** section 17's limit is set from the week of nights that produces a distribution",
            "**Done when** months of resolved setups are in hand",
        };

        foreach (var condition in caught)
        {
            Assert.True(
                Accumulations.Any(rule => rule.Pattern.IsMatch(condition)),
                $"The guard let this through: {condition}");
        }
    }

    [Fact]
    public void TheGuardLeavesAProducibleConditionAlone()
    {
        // The other direction, and the one that decides whether the check is
        // usable. A matcher that catches every mention of a night would make
        // the rule unwritable, since most of what this system does is a night.
        // One night, one session and one run are all produced from inside a
        // session and none of them is waiting.
        var permitted = new[]
        {
            "**Done when** a night runs end to end over the fixture and exits non-zero with a named step on any failure",
            "**Done when** every index member holds a full year of bars with no gaps",
            "**Done when** the mutation is the window boundary moved by one session",
            "**Done when** a shadow candidate is evaluated on nights no live reason fired",
            "**Done when** the earnings rule fires inside the twenty-session horizon and not outside it",
            "**Done when** the page count is measured over two universe sizes and shown not to grow with the population",
            "all phase-5 rows PASS, every stored listing carrying its entry, stop and target, and the nightly schedule registered as a command an operator runs rather than as an instruction to follow",
        };

        foreach (var condition in permitted)
        {
            var hit = Accumulations.FirstOrDefault(rule => rule.Pattern.IsMatch(condition));

            Assert.True(
                hit.Pattern is null,
                $"The guard refused a producible condition as '{hit.Name}': {condition}");
        }
    }
}
