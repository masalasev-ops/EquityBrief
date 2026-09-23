using System.Text.RegularExpressions;

namespace EquityBrief.Tests.Checks;

// record-append-only. Every entry heading ever present in docs/PROGRESS.md is
// still present, read from the history rather than from anyone's memory.
//
// It sits beside changelog-reconciles rather than inside it. That check reads
// the five specs and PROGRESS.md is a record, so no check covered a deletion
// from one, and a check named for the changelog that also guarded a record
// would be a name that stopped describing its scope.
//
// CLAUDE.md declares PROGRESS.md append only, with corrections as new dated
// entries. The rule was a stated property with no instrument behind it until
// now, which is the same shape as the phase report having no failing branch and
// the weighted-call stop's third clause going unasserted.
//
// Headings rather than a line count. An entry's body is corrected by a new
// dated entry rather than by editing, but a reflow inside one would move the
// line count without losing anything, and a check that reads the count would
// report that as a removal. A heading is the unit the record is made of.
public class RecordAppendOnly
{
    const string Record = "docs/PROGRESS.md";

    // The one removal this repository has made, named here with the commit that
    // made it and the reason, so the guard's window is visible rather than the
    // removal sitting silently outside it.
    //
    // PR #37 reverted the 3.7 handover addendum at the operator's direction, on
    // the operator's own record, and CI stayed green because nothing guarded the
    // rule. The phase 3 sign-off recorded that the reasoning survives only in
    // the commit message of 11708c9. Exempting it by name is what lets the check
    // start from the history as it stands rather than from a baseline commit,
    // which would put every earlier removal outside the walk without saying so.
    internal const string Reverted =
        "Addendum to 3.7 - the handover to the phase 3 sign-off 2026-09-09";

    internal static IReadOnlyList<string> HeadingsIn(string record) =>
        Regex.Matches(record, @"^### (.+)$", RegexOptions.Multiline)
            .Select(match => Regex.Replace(match.Groups[1].Value, @"\s+", " ").Trim())
            .ToArray();

    [Fact]
    public void EveryEntryHeadingEverWrittenIsStillThere()
    {
        var git = Shell.Locate("git");

        if (git is null)
        {
            Assert.Fail("No git on PATH. This check reads the history, so it cannot run without one.");
        }

        // Only the commits that touched the file, so the walk is over the
        // population that can remove a heading rather than over every commit.
        var log = Shell.Run(git, ["log", "--format=%H", "--reverse", "--", Record]);

        Assert.Equal(0, log.ExitCode);

        var commits = log.StandardOutput
            .Split((char)10, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .ToArray();

        Assert.True(
            commits.Length >= 5,
            $"Read {commits.Length} commits touching {Record}, expected at least 5. " +
            "A shallow clone returns one, and a walk that cannot see the history must fail " +
            "rather than assert over what it can see.");

        // The high-water mark is over the set of headings rather than over their
        // count, which is what changelog-reconciles could not do: an entry there
        // has no name of its own to key on. Held as a set, the check names the
        // entry that went rather than reporting that one did.
        var everWritten = new Dictionary<string, string>(StringComparer.Ordinal);
        var read = 0;

        foreach (var commit in commits)
        {
            var show = Shell.Run(git, ["show", $"{commit}:{Record}"]);

            Assert.True(
                show.ExitCode == 0,
                $"git show exited {show.ExitCode} for {commit}:{Record}. A revision whose content " +
                $"could not be read is not a revision that lost nothing. {show.StandardError}");

            read++;

            foreach (var heading in HeadingsIn(show.StandardOutput))
            {
                if (!everWritten.ContainsKey(heading))
                {
                    everWritten[heading] = commit;
                }
            }
        }

        var now = HeadingsIn(Corpus.Read(Record));

        // Two scopes. The revisions read is a fact about how often the record has
        // been appended to and is context. The property is the comparison below,
        // and the floor under the high-water mark is what stops a run that read
        // nothing from passing on nought against nought.
        Assert.True(
            everWritten.Count >= 50,
            $"The record's high-water mark is {everWritten.Count} entry headings, expected at least 50, " +
            $"over {read} revisions read.");

        var lost = everWritten
            .Where(entry => !now.Contains(entry.Key, StringComparer.Ordinal))
            .Select(entry => $"\"{entry.Key}\", written at {entry.Value}")
            .ToArray();

        // Both directions on the exemption. A removal that is not the named one
        // fails, and the named one having been restored also fails, because an
        // exemption for a removal that is no longer there is an exemption
        // nothing reads.
        Assert.True(
            lost.Length == 1,
            $"{Record} has lost {lost.Length} entry headings, expected exactly the one named in this " +
            $"check: {string.Join("; ", lost)}. The record is append only and a correction is a new " +
            "dated entry, so restore what went rather than widening this exemption.");

        Assert.True(
            lost[0].StartsWith($"\"{Reverted}\"", StringComparison.Ordinal),
            $"The one absent heading is {lost[0]}, and the only removal this check exempts is " +
            $"\"{Reverted}\", reverted at 11708c9 at the operator's direction.");
    }

    // The other record held as a high-water mark, over decision names.
    //
    // A decision is changed only by another decision, which names what it
    // supersedes and moves the old entry to "Previously decided" with its
    // reasoning intact. 5.0 superseded "News arrives in one dated feed request
    // and is attributed to names locally" and deleted it instead, and nothing
    // noticed: `no-superseded-citation` asks whether a citation resolves to a
    // superseded entry, which a deleted one never can. The phase 5 sign-off
    // restored it, and from there every name DECISIONS.md has ever held is in
    // it, so this starts from the history as it stands with no exemption.
    const string Decisions = "docs/DECISIONS.md";

    internal static IReadOnlyList<string> DecisionNamesIn(string record) =>
        Regex.Matches(record, @"^\*\*(.+?)\*\*", RegexOptions.Multiline)
            .Select(match => match.Groups[1].Value.Trim())
            .ToArray();

    [Fact]
    public void EveryDecisionNameEverWrittenIsStillThere()
    {
        var git = Shell.Locate("git");

        if (git is null)
        {
            Assert.Fail("No git on PATH. This check reads the history, so it cannot run without one.");
        }

        var log = Shell.Run(git, ["log", "--format=%H", "--reverse", "--", Decisions]);

        Assert.Equal(0, log.ExitCode);

        var commits = log.StandardOutput
            .Split((char)10, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .ToArray();

        Assert.True(
            commits.Length >= 5,
            $"Read {commits.Length} commits touching {Decisions}, expected at least 5. A shallow " +
            "clone returns one, and a walk that cannot see the history must fail rather than assert " +
            "over what it can see.");

        var everWritten = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var commit in commits)
        {
            var show = Shell.Run(git, ["show", $"{commit}:{Decisions}"]);

            Assert.True(
                show.ExitCode == 0,
                $"git show exited {show.ExitCode} for {commit}:{Decisions}. {show.StandardError}");

            foreach (var name in DecisionNamesIn(show.StandardOutput))
            {
                everWritten.TryAdd(name, commit);
            }
        }

        // The floor carries the property's population: a walk that read no
        // names would pass on nought against nought. 118 when it was set.
        Assert.True(everWritten.Count >= 90, $"The decision record's high-water mark is {everWritten.Count} names, expected at least 90.");

        var now = DecisionNamesIn(Corpus.Read(Decisions));

        var lost = everWritten
            .Where(entry => !now.Contains(entry.Key, StringComparer.Ordinal))
            .Select(entry => $"\"{entry.Key}\", written at {entry.Value}")
            .ToArray();

        Assert.True(
            lost.Length == 0,
            $"{Decisions} has lost {lost.Length} decision name(s): {string.Join("; ", lost)}. A superseded " +
            "decision moves to \"Previously decided\" with its reasoning, and is never deleted.");
    }

    [Fact]
    public void TheDecisionReaderFindsANameThatWent()
    {
        const string before =
            "**Bars are never interpolated** A gap stops computation.\n\n" +
            "**News arrives in one dated feed request** One request.\n";

        const string moved =
            "**Bars are never interpolated** A gap stops computation.\n\n## Previously decided\n\n" +
            "**News arrives in one dated feed request** Superseded.\n";

        const string deleted = "**Bars are never interpolated** A gap stops computation.\n";

        Assert.Equal(["Bars are never interpolated", "News arrives in one dated feed request"], DecisionNamesIn(before));

        // Moved to "Previously decided" keeps the name, which is what the rule
        // asks; deleted loses it, which is what 5.0 did.
        Assert.Equal(DecisionNamesIn(before), DecisionNamesIn(moved));
        Assert.DoesNotContain("News arrives in one dated feed request", DecisionNamesIn(deleted));

        // Bold in the middle of a line is emphasis, not a name.
        Assert.Empty(DecisionNamesIn("A rule that is **not** a decision name.\n"));
    }

    [Fact]
    public void TheCheckReportsARecordThatLostAnEntry()
    {
        // The permanent proof, over constructed revisions rather than a break and
        // revert done by hand once. The first pair is what the walk above finds
        // when a heading goes; the second is what it finds when a body is
        // corrected under a heading that stays, which is the case the check must
        // not flag.
        const string full =
            "### 1.1 - the membership loader                    2026-09-08\nbuilt something\n" +
            "### 1.2 - the one-year backfill                    2026-09-08\nbuilt something else\n";

        const string gutted =
            "### 1.1 - the membership loader                    2026-09-08\nbuilt something\n";

        const string reflowed =
            "### 1.1 - the membership loader                    2026-09-08\nbuilt something,\nover two lines\n" +
            "### 1.2 - the one-year backfill                    2026-09-08\nbuilt something else\n";

        var before = HeadingsIn(full);

        Assert.Equal(2, before.Count);
        Assert.Single(HeadingsIn(gutted));
        Assert.DoesNotContain(before[1], HeadingsIn(gutted), StringComparer.Ordinal);

        // A body that grew leaves both headings standing, so the check reads the
        // record rather than the file's size.
        Assert.Equal(before, HeadingsIn(reflowed));

        // The run of spaces between an entry's subject and its date varies with
        // the subject's length, so headings are compared with whitespace
        // collapsed. Without that every entry would read as its own heading the
        // first time somebody aligned a column.
        Assert.Equal(
            "1.1 - the membership loader 2026-09-08",
            HeadingsIn("###   1.1 - the membership loader\t\t2026-09-08  \n")[0]);
    }

    // What an entry writes where its sweep's result goes before the sweep has
    // run. An entry is written before the run that verifies it, so the record
    // carries this for as long as it takes to run, and an entry that keeps it
    // afterwards has a prediction nothing answered.
    internal const string Placeholder = "Results: FILLED IN AFTER THE SWEEP.";

    // The entries whose sweep results were never written into them, named with
    // their reason so the window is visible rather than the placeholders sitting
    // silently outside a guard.
    //
    // Each was left unfilled when it was written. The three of phase 9 below
    // have their real results in dated corrections of their own; the rest were
    // never run into the record at all. All eight then had the placeholder
    // overwritten by a fill aimed at one entry that matched every entry, and
    // were restored by the correction that names them.
    internal static readonly string[] Unrecorded =
    [
        "5.8 - correction: the risks are drawn one part to a risk with what would confirm it beneath it, rather than as one run of prose 2026-09-20",
        "5.8 - correction: a reason on tonight's list opens on the values the night measured it over, rather than holding them in an attribute a click never reaches 2026-09-20",
        "5.8 - correction: a reason's values are drawn under the pointer and go as it leaves, rather than needing a click to open and another to put away 2026-09-20",
        "6.5 - correction: a name holding nothing but the key under each figure is missing its research rather than standing as written 2026-09-20",
        "9.1 - each row on tonight's list says whether the name holds research, and the link is drawn with the words of whatever it opens 2026-09-20",
        "9.2 - the request store, both surfaces writing an ask, and the worker draining it oldest first 2026-09-20",
        "9.3 - the queue screen, and taking a report out of it 2026-09-20",
        "9.4 - the report generation lane, stated and half refused 2026-09-20",
    ];

    // One entry, from its heading to the next. The heading is collapsed the way
    // HeadingsIn collapses it, so an entry is named the same by both readers.
    internal static IReadOnlyList<(string Heading, string Body)> Entries(string record) =>
        [.. Regex
            .Matches(record, @"^### (?<heading>[^\r\n]*)(?<body>(?:(?!^### )[\s\S])*)", RegexOptions.Multiline)
            .Select(match => (
                Regex.Replace(match.Groups["heading"].Value, @"\s+", " ").Trim(),
                match.Groups["body"].Value))];

    // A `Results:` label and the indented lines under it, collapsed to one line
    // so an entry reflowed to a different width reads as the same result.
    static readonly Regex ResultBlock = new(
        @"^[ ]*Results:(?<text>[^\r\n]*(?:\r?\n[ ]{12,}[^\r\n]*)*)",
        RegexOptions.Multiline);

    internal static string? ResultIn(string body)
    {
        var block = ResultBlock.Match(body);

        return block.Success
            ? Regex.Replace("Results:" + block.Groups["text"].Value, @"\s+", " ").Trim()
            : null;
    }

    // Two sweeps run over two trees and are written up by whoever ran them, so
    // two entries cannot carry the same result. An identical pair is not a
    // coincidence: it is the signature of a fill aimed at one entry that matched
    // every entry carrying the same placeholder, and it is invisible from inside
    // either entry, because a result naming another tree's run still reads as a
    // result. Nothing read the record for it, so a phase's sweep could report a
    // later phase's numbers and pass every gate.
    [Fact]
    public void NoTwoEntriesRecordTheSameSweepResult()
    {
        var recorded = Entries(Corpus.Read(Record))
            .Select(entry => (entry.Heading, Result: ResultIn(entry.Body)))
            .Where(entry => entry.Result is not null && entry.Result != Placeholder)
            .ToArray();

        // Scope, stated in advance: the entries that record a sweep's result.
        // It is the population the property is over, and it only grows.
        Assert.True(
            recorded.Length >= 90,
            $"Read {recorded.Length} recorded sweep result(s), expected at least 90.");

        var shared = recorded
            .GroupBy(entry => entry.Result, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group =>
                $"{group.Count()} entries record one result word for word, among them " +
                $"\"{group.First().Heading}\" and \"{group.Last().Heading}\"")
            .ToArray();

        Assert.True(
            shared.Length == 0,
            string.Join("\n", shared) + "\nA sweep is run over one tree and written up once, so two " +
            "entries sharing a result means one of them was filled by an edit meant for the other.");
    }

    // The same fault read from the other side. An entry states the baseline its
    // sweep ran against, and its own Verified line states the run that produced
    // its figures; both are about that entry's tree, so they are one number.
    [Fact]
    public void NoEntryStatesASweepBaselineThatDisagreesWithItsOwnRun()
    {
        var read = new List<string>();
        var wrong = new List<string>();

        foreach (var (heading, body) in Entries(Corpus.Read(Record)))
        {
            var baseline = Regex.Match(body, @"[Tt]he baseline is (?<count>\d+) of \k<count>\b");

            if (!baseline.Success)
            {
                continue;
            }

            var own = Regex.Matches(body, @"(?<count>\d+) of \k<count> tests\b")
                .Select(match => match.Groups["count"].Value)
                .ToArray();

            if (own.Length == 0)
            {
                continue;
            }

            read.Add(heading);

            if (!own.Contains(baseline.Groups["count"].Value, StringComparer.Ordinal))
            {
                wrong.Add(
                    $"\"{heading}\" states a sweep baseline of {baseline.Groups["count"].Value} " +
                    $"over a run of {string.Join(" and ", own)} tests");
            }
        }

        // Scope, stated in advance. It is small because this is the form recent
        // entries write a baseline in, and it is the context rather than the
        // property, which is that none of them disagrees.
        Assert.True(read.Count >= 3, $"Read {read.Count} entr(ies) stating a sweep baseline, expected at least 3.");

        Assert.True(
            wrong.Count == 0,
            string.Join("\n", wrong) + "\nA sweep's baseline is the tree it ran over, which is the " +
            "tree the entry's own run measured.");
    }

    // An entry is written before the run that verifies it, so the newest entry
    // may stand with its result unwritten while that run happens. Every other
    // one carrying a placeholder is a prediction nothing answered, and the eight
    // this repository has are named above rather than tolerated by a count.
    [Fact]
    public void AnUnfilledSweepResultStandsOnlyInTheNewestEntryOrTheOnesNamedHere()
    {
        var entries = Entries(Corpus.Read(Record));

        Assert.True(entries.Count >= 50, $"Read {entries.Count} entries, expected at least 50.");

        var newest = entries[^1].Heading;

        var unfilled = entries
            .Where(entry => ResultIn(entry.Body) == Placeholder)
            .Select(entry => entry.Heading)
            .ToArray();

        var unexpected = unfilled
            .Where(heading => heading != newest && !Unrecorded.Contains(heading, StringComparer.Ordinal))
            .ToArray();

        Assert.True(
            unexpected.Length == 0,
            $"{unexpected.Length} entr(ies) carry an unwritten sweep result and are neither the newest " +
            $"nor named in this check: {string.Join("; ", unexpected.Select(heading => $"\"{heading}\""))}. " +
            "Write the sweep's result into the entry rather than widening this exemption.");

        // The other direction. An exemption for an entry that no longer carries
        // one is an exemption nothing reads, so filling one means removing it
        // from the list in the same commit.
        var filled = Unrecorded
            .Where(heading => !unfilled.Contains(heading, StringComparer.Ordinal))
            .ToArray();

        Assert.True(
            filled.Length == 0,
            $"{filled.Length} entr(ies) named here as unwritten now carry a result: " +
            $"{string.Join("; ", filled.Select(heading => $"\"{heading}\""))}. Remove each from the list.");
    }

    // The readers, shown to find each fault and to leave a sound record alone.
    // Without this a reader that matched nothing would pass all three assertions
    // over any record at all.
    [Fact]
    public void TheReadersFindASharedResultAnUnfilledOneAndAMismatchedBaseline()
    {
        const string Sound =
            "### 1.1 - a checkpoint   2026-09-08\n" +
            "Mutated:    a rule.\n" +
            "            Results: M1 red in the test predicted for it. The baseline is 10 of 10.\n" +
            "Verified:   `tools/ci.ps1` green, 10 of 10 tests ran.\n" +
            "\n" +
            "### 1.2 - another checkpoint   2026-09-09\n" +
            "Mutated:    another rule.\n" +
            "            Results: M1 red in the one test that reads the column.\n" +
            "Verified:   `tools/ci.ps1` green, 11 of 11 tests ran.\n";

        var sound = Entries(Sound);

        Assert.Equal(2, sound.Count);
        Assert.Equal("1.1 - a checkpoint 2026-09-08", sound[0].Heading);

        // The block is read whole and collapsed, so a wrapped result is one string.
        Assert.Equal(
            "Results: M1 red in the test predicted for it. The baseline is 10 of 10.",
            ResultIn(sound[0].Body));

        Assert.Null(ResultIn("Built:      something with no sweep at all.\n"));

        // A placeholder is told from a result.
        Assert.Equal(Placeholder, ResultIn("Mutated:    a rule.\n            " + Placeholder + "\n"));

        // Two entries sharing a result, which is what a fill matching every
        // entry leaves behind.
        var shared = Entries(Sound.Replace(
            "Results: M1 red in the one test that reads the column.",
            "Results: M1 red in the test predicted for it. The baseline is 10 of 10."));

        Assert.Equal(ResultIn(shared[0].Body), ResultIn(shared[1].Body));

        // And a baseline that names another tree's run.
        var mismatched = Entries(Sound.Replace("The baseline is 10 of 10.", "The baseline is 99 of 99."))[0];

        Assert.Matches(@"[Tt]he baseline is (?<count>\d+) of \k<count>\b", mismatched.Body);
        Assert.DoesNotContain(
            "99",
            Regex.Matches(mismatched.Body, @"(?<count>\d+) of \k<count> tests\b").Select(match => match.Groups["count"].Value));
    }
}
