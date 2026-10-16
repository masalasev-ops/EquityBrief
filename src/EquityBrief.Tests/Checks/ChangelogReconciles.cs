using System.Text.RegularExpressions;

namespace EquityBrief.Tests.Checks;

// changelog-reconciles. Every commit that deleted a line from a spec also
// changed CHANGELOG.md, and the record only ever grows, both read from the
// history rather than from anyone's memory.
//
// The second property is here because the first cannot see the fault it was
// written for. "Changed CHANGELOG.md" is satisfied by a commit that deletes
// twenty-three entries from it, which is what 8ac2442 did while staying green:
// the record of every clean spec edit phase 0 made went from twenty-six entries
// to three, and the check that exists to protect that record counted it as
// compliance. An entry is prior text and prior text is never edited, so the
// count falling is a defect on its face and needs no judgment to detect.
public class ChangelogReconciles
{
    const string Changelog = "docs/CHANGELOG.md";

    // An entry heading, which is what the file's own format section specifies.
    // The template inside the fenced block is not one, so the date is required.
    const string EntryHeading = @"^### [0-9]{4}-[0-9]{2}-[0-9]{2} ";

    internal static int EntriesIn(string changelog) =>
        Regex.Matches(changelog, EntryHeading, RegexOptions.Multiline).Count;

    // One commit's `git show --numstat` output: the paths it touched, and whether
    // it deleted a line from a spec or a rules file.
    internal static (IReadOnlyList<string> Touched, bool DeletedFromSpec) Numstat(string output)
    {
        var touched = new List<string>();
        var deletedFromSpec = false;

        foreach (var line in output.Split((char)10, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t');

            if (parts.Length != 3)
            {
                continue;
            }

            touched.Add(parts[2].Trim());

            // The rules files are spec-kind, so a commit deleting a line from
            // one owes a changelog entry as a commit deleting from CLAUDE.md
            // does. They carry CLAUDE.md's own text and the prior text of an
            // edit to them is the prior text of a rule.
            if (Corpus.SpecsAndRules.Contains(parts[2].Trim(), StringComparer.Ordinal)
                && int.TryParse(parts[1], out var deleted) && deleted > 0)
            {
                deletedFromSpec = true;
            }
        }

        return (touched, deletedFromSpec);
    }

    [Fact]
    public void EveryCommitThatDeletedASpecLineChangedTheChangelog()
    {
        var git = Shell.Locate("git");

        if (git is null)
        {
            Assert.Fail("No git on PATH. This check reads the history, so it cannot run without one.");
        }

        var log = Shell.Run(git, ["log", "--format=%H", "--reverse"]);

        Assert.Equal(0, log.ExitCode);

        var commits = log.StandardOutput
            .Split((char)10, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .ToArray();

        // Scope, stated in advance and with a floor that only grows.
        Assert.True(commits.Length >= 5, $"Read {commits.Length} commits, expected at least 5.");

        var offenders = new List<string>();
        var deleting = 0;

        foreach (var commit in commits)
        {
            var stat = Shell.Run(git, ["show", "--numstat", "--format=", commit]);

            // A failed invocation returns empty output, the commit reads as one
            // that deleted nothing, and the run stays green. Same shape as the
            // shallow clone that made git log return one commit where the
            // working machine has twenty.
            Assert.True(
                stat.ExitCode == 0,
                $"git show exited {stat.ExitCode} for {commit}. A commit whose diff could not be " +
                $"read is not a commit that deleted nothing. {stat.StandardError}");

            var (touched, deletedFromSpec) = Numstat(stat.StandardOutput);

            if (!deletedFromSpec)
            {
                continue;
            }

            deleting++;

            if (!touched.Contains(Changelog, StringComparer.Ordinal))
            {
                offenders.Add(commit);
            }
        }

        // The population that carries the property is the commits that deleted
        // a spec line, not the commits. A run where that number is zero has
        // asserted nothing, so it is stated rather than folded into the total.
        Assert.True(deleting >= 1, $"{deleting} commits deleted a spec line, expected at least 1.");

        // The message says what a deletion is, because the commits that trip
        // this are usually not the ones that removed anything. git counts a
        // rewritten line as one deletion and one addition, so adding a sentence
        // to the middle of a paragraph deletes the line it replaced. Without
        // this line the next person to hit it reads the check's name, sees a
        // commit that only added prose, and goes looking for the bug here.
        Assert.True(
            offenders.Count == 0,
            "These commits deleted a line from a spec without changing " + Changelog + ": " +
            string.Join(", ", offenders) +
            ". A modified line counts as a deletion, so a commit that only added a sentence to an " +
            "existing paragraph trips this: the line it replaced is gone and its prior text is " +
            "owed here. The entry belongs in the same commit, not the one before it.");
    }

    [Fact]
    public void TheChangelogOnlyEverGrows()
    {
        var git = Shell.Locate("git");

        if (git is null)
        {
            Assert.Fail("No git on PATH. This check reads the history, so it cannot run without one.");
        }

        // Only the commits that touched the file or its archive, so the walk is
        // over the population that can move the count rather than over every
        // commit.
        var archive = Corpus.ArchiveOf(Changelog)!;
        var log = Shell.Run(git, ["log", "--format=%H", "--reverse", "--", Changelog, archive.File]);

        Assert.Equal(0, log.ExitCode);

        var commits = log.StandardOutput
            .Split((char)10, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .ToArray();

        Assert.True(
            commits.Length >= 5,
            $"Read {commits.Length} commits touching {Changelog}, expected at least 5. " +
            "A shallow clone returns one, and a walk that cannot see the history must fail " +
            "rather than assert over what it can see.");

        // From the commit that wrote the archive on, a revision's entries are its
        // live file's and its archive's together.
        var move = RecordAppendOnly.MoveOf(git, archive);
        var since = Shell.Run(git, ["rev-list", "--ancestry-path", $"{move}..HEAD"]);

        Assert.Equal(0, since.ExitCode);

        var holding = since.StandardOutput
            .Split((char)10, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Append(move)
            .ToHashSet(StringComparer.Ordinal);

        // The property is asserted against the high-water mark rather than
        // against each revision's predecessor. Both would have gone red at
        // 8ac2442, but a per-revision comparison stays red forever, because that
        // commit is on main and history is not rewritten, so the only way to
        // green would be to exempt the very commit the check exists to catch.
        // Against the high-water mark the check goes red when entries are lost
        // and green again when they are put back, which is a check on the record
        // as it stands rather than on a commit that cannot be unmade.
        var read = 0;

        var (highest, setAt) = HighestOf(commits.Select(commit =>
        {
            read++;

            return (
                commit,
                RecordAppendOnly.Show(git, commit, Changelog),
                holding.Contains(commit) ? RecordAppendOnly.Show(git, commit, archive.File) : null);
        }));

        var now = EntriesIn(Corpus.Read(Changelog));

        // Two scopes. The revisions read is a fact about how often the file has
        // been touched and is context. The property is the one comparison that
        // matters, and the floor under the high-water mark is what stops a run
        // that read nothing from passing on nought against nought.
        Assert.True(highest >= 20, $"The record's high-water mark is {highest} entries, expected at least 20, over {read} revisions read.");

        Assert.True(
            now >= highest,
            $"{Changelog} holds {now} entries and held {highest} at {setAt}. An entry is prior text " +
            "and prior text is never edited, so a record that has lost entries is a defect on its " +
            "face. Restore them from the revision named rather than lowering this expectation.");
    }

    // The most entries the changelog held at any revision and the revision that held them, a revision's entries its
    // live file's and, where it holds the archive, the archive's beside them, so an entry written after the move and
    // removed later stands below a revision that held it.
    internal static (int Highest, string At) HighestOf(IEnumerable<(string Commit, string Live, string? Archive)> revisions)
    {
        var highest = 0;
        var at = string.Empty;

        foreach (var (commit, live, archive) in revisions)
        {
            var entries = EntriesIn(live) + (archive is null ? 0 : EntriesIn(archive));

            if (entries <= highest)
            {
                continue;
            }

            highest = entries;
            at = commit;
        }

        return (highest, at);
    }

    [Fact]
    public void TheHighWaterMarkCountsTheArchiveBesideTheLiveFileFromTheMoveOn()
    {
        const string Template = "```" + "\n### YYYY-MM-DD - <file> - <what changed>\n" + "```\n";

        static string Entries(params string[] days) =>
            string.Concat(days.Select(day => $"### {day} - a.md - an edit\n"));

        // Three entries before the move; the move keeps one and archives two; one is written after it.
        (string, string, string?)[] revisions =
        [
            ("before", Template + Entries("2026-10-12", "2026-10-11", "2026-10-10"), null),
            ("move", Template + Entries("2026-10-12"), Entries("2026-10-11", "2026-10-10")),
            ("later", Template + Entries("2026-10-17", "2026-10-12"), Entries("2026-10-11", "2026-10-10")),
        ];

        Assert.Equal((4, "later"), HighestOf(revisions));

        // Read over the live file alone the mark stays at the revision before the move, so the entry written after
        // it could be removed again with nothing falling below the mark.
        Assert.Equal((3, "before"), HighestOf(revisions.Select(revision => (revision.Item1, revision.Item2, (string?)null))));
    }

    [Fact]
    public void TheCheckReportsARecordThatLostEntries()
    {
        // The permanent proof, over constructed revisions rather than over a
        // break and revert done by hand once. The first assertion is what the
        // history walk above would have found at 8ac2442; the second is what it
        // finds on every other commit, so this is not a reader that flags
        // everything.
        const string template = "```" + "\n### YYYY-MM-DD - <file> - <what changed>\n" + "```";
        var full = template + "\n### 2026-09-05 - a.md - one\n### 2026-09-06 - b.md - two\n";
        var gutted = template + "\n### 2026-09-05 - a.md - one\n";

        Assert.Equal(2, EntriesIn(full));
        Assert.Equal(1, EntriesIn(gutted));
        Assert.True(EntriesIn(gutted) < EntriesIn(full));

        // The template heading inside the fenced block is not an entry, or the
        // count would be one higher than the file's own format section says.
        Assert.Equal(0, EntriesIn(template));
    }
}
