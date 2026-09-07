namespace EquityBrief.Tests.Checks;

// changelog-reconciles. Every commit that deleted a line from a spec also
// changed CHANGELOG.md, read from the history rather than from anyone's memory.
public class ChangelogReconciles
{
    const string Changelog = "docs/CHANGELOG.md";

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
}
