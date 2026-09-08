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

            var touched = new List<string>();
            var deletedFromSpec = false;

            foreach (var line in stat.StandardOutput.Split((char)10, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('\t');

                if (parts.Length != 3)
                {
                    continue;
                }

                touched.Add(parts[2].Trim());

                if (Corpus.Specs.Contains(parts[2].Trim(), StringComparer.Ordinal)
                    && int.TryParse(parts[1], out var deleted) && deleted > 0)
                {
                    deletedFromSpec = true;
                }
            }

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
        Assert.Empty(offenders);
    }

    [Fact]
    public void TheChangelogOnlyEverGrows()
    {
        var git = Shell.Locate("git");

        if (git is null)
        {
            Assert.Fail("No git on PATH. This check reads the history, so it cannot run without one.");
        }

        // Only the commits that touched the file, so the walk is over the
        // population that can move the count rather than over every commit.
        var log = Shell.Run(git, ["log", "--format=%H", "--reverse", "--", Changelog]);

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

        // The property is asserted against the high-water mark rather than
        // against each revision's predecessor. Both would have gone red at
        // 8ac2442, but a per-revision comparison stays red forever, because that
        // commit is on main and history is not rewritten, so the only way to
        // green would be to exempt the very commit the check exists to catch.
        // Against the high-water mark the check goes red when entries are lost
        // and green again when they are put back, which is a check on the record
        // as it stands rather than on a commit that cannot be unmade.
        var highest = 0;
        var setAt = string.Empty;
        var read = 0;

        foreach (var commit in commits)
        {
            var show = Shell.Run(git, ["show", $"{commit}:{Changelog}"]);

            Assert.True(
                show.ExitCode == 0,
                $"git show exited {show.ExitCode} for {commit}:{Changelog}. A revision whose content " +
                $"could not be read is not a revision that lost nothing. {show.StandardError}");

            var entries = EntriesIn(show.StandardOutput);
            read++;

            if (entries <= highest)
            {
                continue;
            }

            highest = entries;
            setAt = commit;
        }

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
