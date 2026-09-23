using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Time;

namespace EquityBrief.Core.Research;

// What starting the worker's drain came to: whether a process was started, and the words
// the press's reply carries either way.
public sealed record DrainStart(bool Started, string Line);

// What a press starts once its request is written. One implementation starts the worker's
// drain; the suite holds one that records what it was asked, so a route can be hosted
// without a pass reaching a model. Held here rather than in the read surface, because the
// read surface and the worker are the two things that start a drain and neither references
// the other.
public interface IDrainLauncher
{
    DrainStart Start();
}

// The worker's drain, started as a process of its own from a copy of the worker's build
// output made for it, and not waited for.
//
// A copy and never the build output itself, because the night builds that output again
// from the checkout, and a drain still writing when the night starts would hold its files
// open, which stops the night's build on Windows. The copy is named by a fingerprint of the
// files it was made from, so a press copies once for each build rather than once for each
// press, and every drain started from one build shares it. A copy nothing has been started
// from for a week is removed at the next press, and one a drain may still be running from
// never is, since no drain runs for a week.
//
// The surface holds no reference to the worker: it starts the worker's own verb, by path,
// as `RUNBOOK.md` shows it run by hand, with the arguments passed as a list and never
// through a shell, and names the store it reads in the environment, which the worker's
// configuration reads over its own file, so the drain writes the store this surface reads.
// see: A press writes a request and starts the worker's drain as a process of its own, and every pass waits for the off-peak hours
public sealed class WorkerDrainLauncher(
    string? checkout,
    string? workerBuild,
    string dataRoot,
    IClock clock,
    Func<ProcessStartInfo, bool>? start = null) : IDrainLauncher
{
    public const string Executable = "dotnet";

    // The worker's assembly inside a build, which `dotnet` runs.
    public const string Assembly = "EquityBrief.Worker.dll";

    public const string Verb = "drain";

    // The folder under the data root the copies are made in.
    public const string CopiesFolder = "drains";

    // How long a copy nothing has been started from is kept.
    public static readonly TimeSpan KeptFor = TimeSpan.FromDays(7);

    // The configuration key as an environment variable, which is how the worker is told
    // which store to write.
    public static readonly string DataRootVariable = StoreLocation.DataRootKey.Replace(":", "__", StringComparison.Ordinal);

    const string SurfaceProject = "EquityBrief.Api";
    const string WorkerProject = "EquityBrief.Worker";

    // The worker's build output beside the surface's own: the same configuration and
    // framework under the worker's project. None where the surface is not running from its
    // own project's build, which is how a surface hosted by the suite, whose build is the
    // suite's, finds no worker to start.
    public static string? WorkerBuildBeside(string? checkout, string surfaceBuild)
    {
        if (checkout is null)
        {
            return null;
        }

        var relative = Path.GetRelativePath(Path.Combine(checkout, "src", SurfaceProject), surfaceBuild);

        return relative == "." || relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? null
            : Path.Combine(checkout, "src", WorkerProject, relative);
    }

    public DrainStart Start()
    {
        if (checkout is null || workerBuild is null)
        {
            return new DrainStart(false, "The worker was not started, because the read surface is not running from its own build inside a checkout, so the request waits for the drain run by hand.");
        }

        if (!File.Exists(Path.Combine(workerBuild, Assembly)))
        {
            return new DrainStart(false, "The worker was not started, because no worker is built beside the read surface, so the request waits for the drain run by hand.");
        }

        string copy;

        try
        {
            copy = CopyOf(workerBuild);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return new DrainStart(false, $"The worker was not started, because its build could not be copied to start from: {failure.Message}");
        }

        try
        {
            return (start ?? Started)(StartInfo(copy))
                ? new DrainStart(true, "The worker has started on the queue, and a pass asked at peak waits for the off-peak rate.")
                : new DrainStart(false, "The worker was not started, because its process did not start.");
        }
        catch (System.ComponentModel.Win32Exception failure)
        {
            return new DrainStart(false, $"The worker was not started: {failure.Message}");
        }
    }

    // How the process is started, stated apart from starting it so what a press would run is
    // asserted without running it.
    public ProcessStartInfo StartInfo(string copy)
    {
        var info = new ProcessStartInfo(Executable)
        {
            UseShellExecute = false,

            // Its own process with no window of its own, so an interrupt sent to the
            // surface's console is not sent to a drain partway through a pass.
            CreateNoWindow = true,
            WorkingDirectory = checkout ?? string.Empty,
        };

        info.ArgumentList.Add(Path.Combine(copy, Assembly));
        info.ArgumentList.Add(Verb);
        info.Environment[DataRootVariable] = dataRoot;

        return info;
    }

    // The copy for this build, made where none is held yet. Made beside its final name and
    // moved into it, so a drain never starts from a copy another press is still writing, and
    // two presses copying the same build at once keep one and discard the other.
    public string CopyOf(string build)
    {
        var copies = Path.Combine(dataRoot, CopiesFolder);
        var copy = Path.Combine(copies, Fingerprint(build));

        if (!Directory.Exists(copy))
        {
            var partial = copy + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".partial";

            foreach (var file in Directory.EnumerateFiles(build, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(partial, Path.GetRelativePath(build, file));

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }

            try
            {
                Directory.Move(partial, copy);
            }
            catch (IOException) when (Directory.Exists(copy))
            {
                Directory.Delete(partial, recursive: true);
            }
        }

        var now = clock.UtcNow.UtcDateTime;

        Directory.SetLastWriteTimeUtc(copy, now);
        Removed(copies, copy, now);

        return copy;
    }

    // A fingerprint of a build: every file's path inside it, its length and when it was last
    // written, so a build the night made again is a different build and one nothing touched
    // is the same one.
    public static string Fingerprint(string build)
    {
        var listed = new StringBuilder();

        foreach (var file in Directory.EnumerateFiles(build, "*", SearchOption.AllDirectories)
                     .Select(file => (Relative: Path.GetRelativePath(build, file).Replace(Path.DirectorySeparatorChar, '/'), Full: file))
                     .OrderBy(file => file.Relative, StringComparer.Ordinal))
        {
            var info = new FileInfo(file.Full);

            listed.Append(file.Relative).Append('|')
                .Append(info.Length.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(listed.ToString())))[..16];
    }

    // Every copy other than this one that nothing has been started from for longer than a
    // copy is kept. One that cannot be removed is left for the next press, since a file a
    // process still holds is the reason a removal fails.
    static void Removed(string copies, string keep, DateTime now)
    {
        foreach (var other in Directory.EnumerateDirectories(copies))
        {
            if (string.Equals(other, keep, StringComparison.Ordinal) || now - Directory.GetLastWriteTimeUtc(other) <= KeptFor)
            {
                continue;
            }

            try
            {
                Directory.Delete(other, recursive: true);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    static bool Started(ProcessStartInfo info)
    {
        using var process = Process.Start(info);

        return process is not null;
    }
}
