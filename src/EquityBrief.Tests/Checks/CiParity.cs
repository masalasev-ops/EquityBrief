using System.Text.RegularExpressions;

namespace EquityBrief.Tests.Checks;

// ci-parity. tools/ci.ps1 and tools/ci.sh run the same steps in the same order,
// and a step that fails fails the script it runs in.
//
// The two are not translations of each other and are not compared as text.
// Windows PowerShell cannot parse &&, so they differ in syntax by necessity.
public class CiParity
{
    // Both files name their steps the same way, which is what makes the
    // sequences comparable without comparing the code around them.
    const string StepPattern = @"^\s*[Ss]tep\s+""([^""]+)""";

    static IReadOnlyList<string> StepsIn(string script) =>
        Regex.Matches(script, StepPattern, RegexOptions.Multiline)
            .Select(match => match.Groups[1].Value)
            .ToArray();

    [Fact]
    public void BothScriptsRunTheSameStepsInTheSameOrder()
    {
        var shell = StepsIn(File.ReadAllText(Repository.Tool("ci.sh")));
        var powerShell = StepsIn(File.ReadAllText(Repository.Tool("ci.ps1")));

        // Scope, stated in advance. Reading no steps out of either file would
        // otherwise make two empty lists compare equal.
        Assert.True(shell.Count >= 6, $"Read {shell.Count} steps from ci.sh, expected at least 6.");

        Assert.Equal(shell, powerShell);
    }

    [Fact]
    public void TheReaderFindsNothingInAScriptWithNoSteps()
    {
        // The permanent proof that the reader is reading, not matching anything.
        Assert.Empty(StepsIn("echo hello\ndotnet build\n"));
        Assert.Equal(["one", "two"], StepsIn("step \"one\" a\nStep \"two\" { b }\n"));
    }

    [Theory]
    [InlineData("ci.sh")]
    [InlineData("ci.ps1")]
    public void AFailingStepFailsTheScriptItRunsIn(string script)
    {
        // The script is copied somewhere with no solution beside it, so the
        // restore step must fail. Nothing is broken in the repository to prove
        // this and nothing has to be put back afterwards.
        using var elsewhere = new TemporaryDirectory();
        var tools = Directory.CreateDirectory(Path.Combine(elsewhere.Path, "tools"));
        var copy = Path.Combine(tools.FullName, script);
        File.Copy(Repository.Tool(script), copy);

        var result = Run(copy, elsewhere.Path);

        if (result is null)
        {
            // The split is the assertion: a Windows machine always has a host
            // for the .ps1, so this branch can only be a bash that is missing
            // on a platform where the .sh is not the entry point anyway.
            Assert.False(OperatingSystem.IsWindows() && script.EndsWith(".ps1", StringComparison.Ordinal));
            return;
        }

        // Both assertions carry the whole transcript. A failure here is about
        // a script that ran somewhere else, so a bare comparison would say only
        // that it did not match and leave nothing to diagnose from.
        Assert.True(
            result.ExitCode != 0,
            $"{script} exited 0 where a step had to fail. Transcript:" + Environment.NewLine + result.Output);

        Assert.True(
            result.Output.Contains("failed at step: restore", StringComparison.Ordinal),
            $"{script} failed but not at the restore step, so something earlier broke first. " +
            $"Exit {result.ExitCode}. Transcript:" + Environment.NewLine + result.Output);
    }

    static ShellResult? Run(string script, string workingDirectory)
    {
        if (script.EndsWith(".ps1", StringComparison.Ordinal))
        {
            var host = Shell.PowerShellHost();

            return host is null
                ? null
                : Shell.Run(
                    host,
                    ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script],
                    workingDirectory);
        }

        var bash = Shell.Bash();

        return bash is null ? null : Shell.Run(bash, [script], workingDirectory);
    }
}
