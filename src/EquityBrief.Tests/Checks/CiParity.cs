using System.Text.RegularExpressions;

namespace EquityBrief.Tests.Checks;

// ci-parity. tools/ci.ps1 and tools/ci.sh run the same steps in the same order,
// a step that fails fails the script it runs in and names itself, and both
// resolve a data root of their own rather than the operator's.
//
// The two are not translations of each other and are not compared as text.
// Windows PowerShell cannot parse &&, so they differ in syntax by necessity.
//
// The data root half arrived at the phase 5 sign-off. 5.7 moved both scripts off
// `data` after they were found dropping the store the schedule fills, and
// repaired the two files without giving the property an instrument: the strings
// `data-ci` and `EquityBrief__DataRoot` appeared nowhere under src/, so a later
// edit to one script alone would have put the fault straight back. The fault was
// latent from 0.4 and fired only once a schedule existed, which is the kind a
// check has to hold rather than a reader.
public class CiParity
{
    // Both files name their steps the same way, which is what makes the
    // sequences comparable without comparing the code around them.
    const string StepPattern = @"^\s*[Ss]tep\s+""([^""]+)""";

    // The one line in each script that sets the data root, and the last segment
    // of what it sets. Read off the assignment rather than off a literal kept
    // here, because a literal here is the second place the path lives.
    const string RootPattern = @"EquityBrief__DataRoot\s*=\s*(?<value>.+)";

    static IReadOnlyList<string> StepsIn(string script) =>
        Regex.Matches(script, StepPattern, RegexOptions.Multiline)
            .Select(match => match.Groups[1].Value)
            .ToArray();

    internal static string? RootSegmentIn(string script)
    {
        var assignment = Regex.Match(script, RootPattern);

        if (!assignment.Success)
        {
            return null;
        }

        // The trailing path segment, quoted in one script and interpolated in
        // the other. Both end the assignment with the segment itself, which is
        // the part that has to agree.
        var segment = Regex.Match(assignment.Groups["value"].Value.Trim(), @"([A-Za-z0-9._-]+)['""]?\s*$");

        return segment.Success ? segment.Groups[1].Value : null;
    }

    // The data root the application resolves when nothing overrides it, read out
    // of the shipped configuration. This is the path the scripts must never
    // drop, and reading it rather than writing it here is what keeps the check
    // correct if the operator's root is ever renamed.
    internal static string ConfiguredRoot()
    {
        var settings = File.ReadAllText(
            Path.Combine(Repository.Root, "src", "EquityBrief.Worker", "appsettings.json"));

        var root = Regex.Match(settings, @"""DataRoot""\s*:\s*""(?<value>[^""]+)""");

        Assert.True(root.Success, "the worker's appsettings.json states no DataRoot to compare the scripts against.");

        return root.Groups["value"].Value.Trim('/', '\\');
    }

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

    [Fact]
    public void BothScriptsDropAStoreOfTheirOwnAndNeitherNamesTheOperators()
    {
        var shell = File.ReadAllText(Repository.Tool("ci.sh"));
        var powerShell = File.ReadAllText(Repository.Tool("ci.ps1"));

        var shellRoot = RootSegmentIn(shell);
        var powerShellRoot = RootSegmentIn(powerShell);

        // Each sets one, which is the half that would have been missing had 5.7
        // repaired only one of the two files.
        Assert.True(shellRoot is not null, "tools/ci.sh sets no EquityBrief__DataRoot, so it runs against whatever the caller's environment holds.");
        Assert.True(powerShellRoot is not null, "tools/ci.ps1 sets no EquityBrief__DataRoot, so it runs against whatever the caller's environment holds.");

        // And they agree, which is the parity the roster row claims beyond step
        // order: two scripts verifying against two different stores are two
        // different instruments.
        Assert.Equal(shellRoot, powerShellRoot);

        // And it is not the store the nightly job fills. Read out of the shipped
        // configuration rather than written here.
        var configured = ConfiguredRoot();

        Assert.NotEqual(configured, shellRoot);
        Assert.NotEqual(configured, powerShellRoot);

        // The step that drops targets the root that was exported, in both. A
        // script exporting one path and deleting another is the fault this pair
        // exists to refuse, seen from the other end.
        Assert.Contains(shellRoot!, DropStatementIn(shell), StringComparison.Ordinal);
        Assert.Contains(powerShellRoot!, DropStatementIn(powerShell), StringComparison.Ordinal);

        Assert.DoesNotContain($" {configured}\n", DropStatementIn(shell), StringComparison.Ordinal);
        Assert.DoesNotContain($" {configured}\n", DropStatementIn(powerShell), StringComparison.Ordinal);
    }

    [Fact]
    public void TheRootReaderIsReadingRatherThanMatchingAnything()
    {
        // The permanent proof, in both directions, because a sweep whose
        // expected result is nothing is passed every time by a matcher that
        // matches nothing at all.
        Assert.Null(RootSegmentIn("dotnet build\nrm -rf data\n"));
        Assert.Equal("data-ci", RootSegmentIn("export EquityBrief__DataRoot=\"$root/data-ci\"\n"));
        Assert.Equal("data-ci", RootSegmentIn("$env:EquityBrief__DataRoot = Join-Path (Get-Location) 'data-ci'\n"));

        // And the form the repair replaced, which the assertion above has to be
        // able to tell from the form that replaced it.
        Assert.Equal("data", RootSegmentIn("export EquityBrief__DataRoot=\"$root/data\"\n"));
        Assert.Equal(ConfiguredRoot(), RootSegmentIn("export EquityBrief__DataRoot=\"$root/data\"\n"));
    }

    // The lines that delete a directory, which is where each script says what it
    // is willing to destroy.
    static string DropStatementIn(string script) =>
        string.Join(
            "\n",
            script.Split('\n')
                .Where(line => line.Contains("rm -rf", StringComparison.Ordinal)
                    || line.Contains("Remove-Item", StringComparison.Ordinal))) + "\n";

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

    [Fact]
    public void ACmdletOnlyStepThatFailsNamesItselfRatherThanDyingUnnamed()
    {
        // The shipped Step function, exercised over the kind of step the
        // existing theory above cannot reach.
        //
        // ci.sh judges every step through `if ! "$@"`, so a failure of any kind
        // prints the named line. ci.ps1 judged a step by $LASTEXITCODE, which a
        // cmdlet never sets, so a cmdlet-only step failed through a terminating
        // error under the Stop preference and the script died before the named
        // line was written. The exit code was right and the message was gone,
        // and "drop the store" is a cmdlet-only step, so this was the one step
        // of six whose failure said nothing.
        //
        // Constructed by taking the shipped script and replacing its step list,
        // so the function under test is the one that ships rather than a copy
        // of it written here.
        var shipped = File.ReadAllText(Repository.Tool("ci.ps1"));
        var steps = Regex.Matches(shipped, StepPattern, RegexOptions.Multiline);

        Assert.True(steps.Count >= 6, $"Read {steps.Count} steps from ci.ps1, expected at least 6.");

        var failing = Regex.Replace(
            shipped,
            StepPattern + ".*$",
            string.Empty,
            RegexOptions.Multiline);

        failing = failing.Replace(
            "    Write-Host ''\n    Write-Host 'ci: green'",
            "    Step \"drop the store\" { Remove-Item -Recurse -Force (Join-Path (Get-Location) 'no-such-directory') }\n\n    Write-Host 'ci: green'",
            StringComparison.Ordinal);

        Assert.Contains("Step \"drop the store\"", failing, StringComparison.Ordinal);

        using var elsewhere = new TemporaryDirectory();
        var tools = Directory.CreateDirectory(Path.Combine(elsewhere.Path, "tools"));
        var copy = Path.Combine(tools.FullName, "ci.ps1");
        File.WriteAllText(copy, failing);

        var result = Run(copy, elsewhere.Path);

        if (result is null)
        {
            Assert.False(OperatingSystem.IsWindows());
            return;
        }

        Assert.True(
            result.ExitCode != 0,
            "ci.ps1 exited 0 where a cmdlet-only step had to fail. Transcript:" + Environment.NewLine + result.Output);

        Assert.True(
            result.Output.Contains("failed at step: drop the store", StringComparison.Ordinal),
            "ci.ps1 failed at a cmdlet-only step without naming it, so a gate that died is indistinguishable " +
            $"from one that ran. Exit {result.ExitCode}. Transcript:" + Environment.NewLine + result.Output);
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
