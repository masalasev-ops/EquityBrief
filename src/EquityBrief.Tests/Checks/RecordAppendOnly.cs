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
}
