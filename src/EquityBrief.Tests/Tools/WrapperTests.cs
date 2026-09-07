namespace EquityBrief.Tests.Tools;

// A .ps1 wrapper hands its work to the one bash script and returns both what
// that script printed and the code it exited with.
//
// Calling an extensionless bash script by name from PowerShell produces no
// output, leaves $LASTEXITCODE unset and leaves $? true, so a gate that never
// executed is indistinguishable from one that passed. These tests are what
// stand between that and a green run.
public class WrapperTests
{
    const string OnStandardOutput = "wrapper-probe: this line is written to stdout";
    const string OnStandardError = "wrapper-probe: this line is written to stderr";
    const int ProbeExitCode = 3;

    [Fact]
    public void TheProbeFailsWithOutputOnBothStreams()
    {
        // What the wrapper has to reproduce. Asserted separately so a failure
        // in the pair below can be told apart from a probe that stopped failing.
        var bash = Shell.Bash();

        if (bash is null)
        {
            Assert.False(OperatingSystem.IsWindows(), "Windows machines here carry a bash.");
            return;
        }

        var result = Shell.Run(bash, [Repository.Tool("wrapper-probe")]);

        Assert.Equal(ProbeExitCode, result.ExitCode);
        Assert.Contains(OnStandardOutput, result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains(OnStandardError, result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWrapperReturnsBothTheOutputAndTheExitCode()
    {
        var host = Shell.PowerShellHost();

        if (host is null)
        {
            // The split is the assertion. On Windows the .ps1 is the documented
            // entry point, so no host being found there is itself a failure.
            Assert.False(OperatingSystem.IsWindows(), "Windows machines carry a PowerShell.");
            return;
        }

        var result = Shell.Run(
            host,
            ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Repository.Tool("wrapper-probe.ps1")]);

        Assert.Equal(ProbeExitCode, result.ExitCode);
        Assert.Contains(OnStandardOutput, result.Output, StringComparison.Ordinal);
        Assert.Contains(OnStandardError, result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void AWrapperOnAMachineWithNoBashExitsWithANamedMessage()
    {
        var host = Shell.PowerShellHost();

        if (host is null)
        {
            Assert.False(OperatingSystem.IsWindows(), "Windows machines carry a PowerShell.");
            return;
        }

        // A path with nothing on it, so the wrapper cannot find a bash. The
        // failure that matters is exiting zero, which would make a gate that
        // never ran look like one that passed.
        using var empty = new TemporaryDirectory();

        var result = Shell.Run(
            host,
            ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Repository.Tool("migrate.ps1")],
            environment: new Dictionary<string, string> { ["PATH"] = empty.Path });

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("No bash found on PATH", result.Output, StringComparison.Ordinal);
        Assert.Contains("tools/migrate", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void AWrapperCalledUnderAStopPreferenceStillExits127AndNamesTheStep()
    {
        var host = Shell.PowerShellHost();

        if (host is null)
        {
            Assert.False(OperatingSystem.IsWindows(), "Windows machines carry a PowerShell.");
            return;
        }

        // Through a caller that sets the preference to Stop, which is what
        // tools/ci.ps1 does before it runs any step. Invoking migrate.ps1
        // directly under the default preference is what made this invisible:
        // Write-Error became a terminating error, exit 127 never ran, and the
        // run died at 1 before the failing step could print its name.
        using var empty = new TemporaryDirectory();
        using var caller = new TemporaryDirectory();

        var script = Path.Combine(caller.Path, "caller.ps1");

        File.WriteAllText(script, string.Join(
            Environment.NewLine,
            "$ErrorActionPreference = 'Stop'",
            "$global:LASTEXITCODE = 0",
            "& '" + Repository.Tool("migrate.ps1") + "'",
            "if ($LASTEXITCODE -ne 0) {",
            "    Write-Host 'ci: failed at step: migrate'",
            "    exit $LASTEXITCODE",
            "}",
            "exit 0"));

        var result = Shell.Run(
            host,
            ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script],
            environment: new Dictionary<string, string> { ["PATH"] = empty.Path });

        Assert.Equal(127, result.ExitCode);
        Assert.Contains("No bash found on PATH", result.Output, StringComparison.Ordinal);
        Assert.Contains("ci: failed at step: migrate", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCallerAboveIsTheContractTheCiScriptRunsStepsUnder()
    {
        // The caller written above is only worth what it reproduces, so what it
        // reproduces is read out of the script rather than remembered.
        var ci = File.ReadAllText(Repository.Tool("ci.ps1"));

        foreach (var line in new[]
                 {
                     "$ErrorActionPreference = 'Stop'",
                     "$global:LASTEXITCODE = 0",
                     "if ($LASTEXITCODE -ne 0)",
                     "ci: failed at step:",
                     "migrate.ps1",
                 })
        {
            Assert.Contains(line, ci, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EveryBashEntryPointHasAPowerShellOneBesideIt()
    {
        var scripts = Directory.GetFiles(Path.Combine(Repository.Root, "tools"))
            .Where(file => !file.EndsWith(".ps1", StringComparison.Ordinal))
            .Where(file => File.ReadLines(file).FirstOrDefault()?.StartsWith("#!", StringComparison.Ordinal) == true)
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToArray();

        Assert.True(scripts.Length >= 3, $"Found {scripts.Length} bash entry points, expected at least 3.");

        var unpaired = scripts
            .Where(script => !File.Exists(Path.ChangeExtension(script, ".ps1")))
            .Select(Path.GetFileName)
            .ToArray();

        Assert.Empty(unpaired);
    }
}
