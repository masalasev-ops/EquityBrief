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
    internal static string? Bash() => Locate("bash");

    // pwsh on both runners; powershell.exe is what a Windows machine has by
    // default. Either can run a .ps1, and the wrapper has to work under both.
    internal static string? PowerShellHost() => Locate("pwsh") ?? Locate("powershell");

    internal static string? Locate(string name)
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
                    return candidate;
                }
            }
        }

        return null;
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

        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new ShellResult(process.ExitCode, standardOutput, standardError);
    }
}
