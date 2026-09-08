using System.Diagnostics;

namespace EquityBrief.Tests;

internal sealed record ShellResult(int ExitCode, string StandardOutput, string StandardError)
{
    internal string Output => StandardOutput + StandardError;
}

// Running the repository's own entry points, so the suite can assert what they
// do rather than what their source looks like.
internal static class Shell
{
    // The first bash on PATH that can read a file in this checkout.
    //
    // Not simply the first bash. On a Windows machine with the optional WSL
    // feature enabled, C:\Windows\System32\bash.exe is the WSL launcher, comes
    // first on PATH, and cannot open a Windows path: it answers with an
    // advertisement for installing a distribution and exit 1. The suite then
    // reports four red tests about the wrappers on a machine where the wrappers
    // are fine, which is a red that says nothing about the build.
    //
    // Chosen by the property rather than by the path, matching tools/run-bash.ps1.
    internal static string? Bash()
    {
        var probe = Path.Combine(Repository.Root, "tools", "run-bash.ps1").Replace('\\', '/');

        foreach (var candidate in BashCandidates())
        {
            try
            {
                if (Run(candidate, ["-c", $"test -f '{probe}'"]).ExitCode == 0)
                {
                    return candidate;
                }
            }
            catch (Exception)
            {
                // A candidate that cannot be started is a candidate that cannot
                // run the script, which is the same answer.
            }
        }

        return null;
    }

    // pwsh on both runners; powershell.exe is what a Windows machine has by
    // default. Either can run a .ps1, and the wrapper has to work under both.
    internal static string? PowerShellHost() => Locate("pwsh") ?? Locate("powershell");

    // Every bash on PATH, then the ones beside git.
    //
    // A default Git for Windows install puts git.exe in cmd\ and bash.exe in
    // bin\, and only cmd\ goes on PATH, so a working bash is present and
    // unreachable by name while the WSL launcher is reachable by name and does
    // not work. Mirrors tools/run-bash.ps1, which solves the same problem for
    // the operator running the documented commands.
    static IEnumerable<string> BashCandidates()
    {
        foreach (var onPath in LocateAll("bash"))
        {
            yield return onPath;
        }

        var root = Path.GetDirectoryName(Path.GetDirectoryName(Locate("git")));

        if (root is null)
        {
            yield break;
        }

        foreach (var relative in new[] { Path.Combine("bin", "bash.exe"), Path.Combine("usr", "bin", "bash.exe") })
        {
            var beside = Path.Combine(root, relative);

            if (File.Exists(beside))
            {
                yield return beside;
            }
        }
    }

    internal static string? Locate(string name) => LocateAll(name).FirstOrDefault();

    internal static IEnumerable<string> LocateAll(string name)
    {
        var extensions = OperatingSystem.IsWindows()
            ? new[] { ".exe", ".cmd", string.Empty }
            : new[] { string.Empty };

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory.Trim('"'), name + extension);

                if (File.Exists(candidate))
                {
                    yield return candidate;
                }
            }
        }
    }

    internal static ShellResult Run(
        string executable,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory ?? Repository.Root,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var (key, value) in environment ?? new Dictionary<string, string>())
        {
            start.Environment[key] = value;
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"Could not start {executable}.");

        // Both streams are read at once, not one to completion and then the
        // other.
        //
        // The obligation carried out of 0.7. Reading standard output to the end
        // first blocks until the child closes it, while the child blocks writing
        // to an error pipe whose buffer is full, and neither moves again. It is
        // latent at the output sizes the suite's children produce today and it
        // is not latent in what this repository is for: `tools/nightly` will run
        // a child whose diagnostics can fill a pipe, and the deadlock would look
        // like a night that hung rather than a night that failed.
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();

        Task.WaitAll(standardOutput, standardError);
        process.WaitForExit();

        return new ShellResult(process.ExitCode, standardOutput.Result, standardError.Result);
    }
}
