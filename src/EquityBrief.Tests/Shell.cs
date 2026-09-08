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
