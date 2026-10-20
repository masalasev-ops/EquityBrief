using EquityBrief.Tests.Checks;

namespace EquityBrief.Tests.Tools;

// tools/remedy runs a remedy file, the worker commands a change owes the operator's store, one step a line
// in order, and every remedy file under tools/remedies/ is made of verbs the worker dispatches. The
// operator's ruling of 2026-09-30, item 8: a remedy is issued the same way every time, from a committed
// file, and never from a script in a session's scratch folder.
public class RemedyTests
{
    const string FirstRemedy = "2026-09-29-12.2-pullbacks-alone.txt";

    static string Remedies => Path.Combine(Repository.Root, "tools", "remedies");

    // The steps of a remedy file as the script reads them: every line that is not blank and not a comment.
    static IReadOnlyList<string> StepsOf(string file) =>
        [.. File.ReadLines(file).Where(line => line.Trim().Length > 0 && !line.TrimStart().StartsWith('#'))];

    [Fact]
    public void EveryRemedyFileIsMadeOfStepsWhoseVerbsTheWorkerDispatches()
    {
        var files = Directory.GetFiles(Remedies, "*.txt").OrderBy(file => file, StringComparer.Ordinal).ToArray();

        Assert.NotEmpty(files);
        Assert.Contains(files, file => Path.GetFileName(file) == FirstRemedy);

        var dispatched = ComponentAccess.DispatchedVerbs(File.ReadAllText(Path.Combine(Repository.Root, "src", "EquityBrief.Worker", "Program.cs")));

        Assert.Contains("version", dispatched);
        Assert.Contains("shape", dispatched);

        foreach (var file in files)
        {
            var steps = StepsOf(file);

            Assert.True(steps.Count > 0, $"{Path.GetFileName(file)} holds no step.");

            foreach (var step in steps)
            {
                var verb = step.Split(' ', 2)[0];

                Assert.True(dispatched.Contains(verb, StringComparer.Ordinal), $"{Path.GetFileName(file)}: '{verb}' is no verb the worker dispatches.");
            }
        }

        // The first remedy committed is the one the operator ran on 2026-09-30: the eight ladder windows
        // closed and opened again, sixteen version steps, and the filter's rule correction.
        var first = StepsOf(Path.Combine(Remedies, FirstRemedy));

        Assert.Equal(17, first.Count);
        Assert.Equal(16, first.Count(step => step.StartsWith("version ", StringComparison.Ordinal)));
        Assert.StartsWith("shape --rule-correction --restarts 0", first[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void TheRemedyScriptListsItsStepsInOrderRefusesUnderANightLockAndRefusesABadStart()
    {
        var bash = Shell.Bash();

        if (bash is null)
        {
            Assert.False(OperatingSystem.IsWindows(), "Windows machines here carry a bash.");
            return;
        }

        var script = Repository.Tool("remedy");
        var file = "tools/remedies/" + FirstRemedy;
        var steps = StepsOf(Path.Combine(Remedies, FirstRemedy));

        // --list prints every step numbered, in the file's order, and runs nothing.
        var listed = Shell.Run(bash, [script, file, "--list"]);

        Assert.Equal(0, listed.ExitCode);
        Assert.Equal(
            steps.Select((step, at) => $"{at + 1} of {steps.Count}: {step}"),
            listed.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r')));
        Assert.DoesNotContain("===", listed.Output, StringComparison.Ordinal);

        // A run is refused while the night's lock file exists under the data root, before any step runs,
        // in a scratch data root and never the repository's own.
        var root = Directory.CreateTempSubdirectory("remedy-lock-");

        try
        {
            File.WriteAllText(Path.Combine(root.FullName, EquityBrief.Core.Configuration.NightLock.FileName), "held by a test");

            var refused = Shell.Run(bash, [script, file], environment: new Dictionary<string, string> { ["EquityBrief__DataRoot"] = root.FullName.Replace('\\', '/') });

            Assert.Equal(3, refused.ExitCode);
            Assert.Contains(EquityBrief.Core.Configuration.NightLock.FileName, refused.StandardError, StringComparison.Ordinal);
            Assert.DoesNotContain("=== 1 of", refused.Output, StringComparison.Ordinal);
        }
        finally
        {
            root.Delete(recursive: true);
        }

        // A start past the last step, a step number that is not one, and a file that is not there are each
        // refused with a named message and exit 2.
        Assert.Equal(2, Shell.Run(bash, [script, file, "--from", "0"]).ExitCode);
        Assert.Equal(2, Shell.Run(bash, [script, file, "--from", "18"]).ExitCode);

        var missing = Shell.Run(bash, [script, "tools/remedies/no-such-remedy.txt", "--list"]);

        Assert.Equal(2, missing.ExitCode);
        Assert.Contains("no such file", missing.StandardError, StringComparison.Ordinal);
    }

    // A step the worker refuses stops the remedy with that step's own exit code and runs no step after it: a register
    // line over a scratch data root holding no store is refused by the worker before it writes, and the runner exits with
    // the same code, never nought, so a refused step reads as one and the second step is never reached.
    [Fact]
    public void ARemedyWhoseStepIsRefusedStopsThereAndExitsWithThatStepsCode()
    {
        var bash = Shell.Bash();

        if (bash is null)
        {
            Assert.False(OperatingSystem.IsWindows(), "Windows machines here carry a bash.");
            return;
        }

        var root = Directory.CreateTempSubdirectory("remedy-refused-");

        try
        {
            var file = Path.Combine(root.FullName, "refused.txt");

            File.WriteAllLines(file,
            [
                "# a constructed remedy of two steps, the first refused",
                "register --retire \"no such rule\" --evidence \"a step the worker refuses\"",
                "register --retire \"another rule\" --evidence \"a step never reached\"",
            ]);

            var data = Path.Combine(root.FullName, "data");

            Directory.CreateDirectory(data);

            var run = Shell.Run(bash, [Repository.Tool("remedy"), file.Replace('\\', '/')], environment: new Dictionary<string, string> { ["EquityBrief__DataRoot"] = data.Replace('\\', '/') });
            const int Refused = 1;
            const int Steps = 2;

            Assert.NotEqual(0, run.ExitCode);
            Assert.Contains(FormattableString.Invariant($"=== {Refused} of {Steps}"), run.Output, StringComparison.Ordinal);
            Assert.DoesNotContain(FormattableString.Invariant($"=== {Steps} of {Steps}"), run.Output, StringComparison.Ordinal);
            Assert.Contains(FormattableString.Invariant($"remedy: stopped at step {Refused} of {Steps} with exit {run.ExitCode}"), run.StandardError, StringComparison.Ordinal);
            Assert.DoesNotContain("remedy done", run.Output, StringComparison.Ordinal);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
