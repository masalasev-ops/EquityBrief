using System.Globalization;

namespace EquityBrief.Core.Configuration;

// A refusal the night's script wrote before any worker existed: when, and why.
public sealed record NightRefusal(DateTimeOffset At, string Reason);

// What the night's script leaves under the data root and where it builds: the folder holding one clean
// copy of the committed code a commit, built once and run by every try of a night, the rest of one and
// the drain it starts; the file naming the commit the newest night ran; and the file a refusal before any
// worker exists is written to, which the pages read in place of a night that never ran. The script writes
// all three and the read surface and the worker read them, so their names and shapes resolve here.
// see: Each night is built from a clean copy of the main checkout's own commit and never from its working tree, and refuses only a checkout off main or ahead of the remote's main
public static class NightBuild
{
    // The folder under the data root the copies are built in, one a commit.
    public const string CopiesFolder = "nights";

    // The file under the data root naming the commit the newest night was built from.
    public const string CommitFileName = "night.build";

    // The file under the data root a refusal is written to: the instant, a space, and the reason.
    public const string RefusalFileName = "night.refused";

    // The run log stage a night records the commit it was built from under.
    public const string Stage = "build";

    // The environment variable naming the secrets file the worker reads by path, which is how a night built
    // from a clean copy, which holds none, reads the main checkout's without a copy of it.
    public const string SecretsFileVariable = "EquityBrief__SecretsFile";

    // The checkout's secrets file, where the night's script names it and where a press names it for the worker
    // it starts from a copy of the night's build.
    public static string SecretsFileIn(string checkout) =>
        Path.Combine(checkout, "src", "EquityBrief.Worker", "appsettings.Secrets.json");

    // The argument the script hands the worker the commit with, and the one with the line it records.
    public const string BuiltFromArgument = "--built-from";
    public const string BuildNoteArgument = "--build-note";

    // The worker's assembly inside a copy's build.
    public const string WorkerAssembly = "EquityBrief.Worker.dll";

    public static string Copies(string dataRoot) => Path.Combine(dataRoot, CopiesFolder);

    // The commit the newest night was built from, or none where no night the script started has run.
    public static string? Commit(string dataRoot)
    {
        var file = Path.Combine(dataRoot, CommitFileName);

        try
        {
            return File.Exists(file) && File.ReadAllText(file).Trim() is { Length: > 0 } commit ? commit : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    // The folder holding the worker's build inside the copy of the commit named, found by the assembly
    // rather than by a path that would state the framework a second time, or none where the copy holds
    // no build.
    public static string? WorkerBuild(string dataRoot, string commit)
    {
        var copy = Path.Combine(Copies(dataRoot), commit.Length > 12 ? commit[..12] : commit);
        var builds = Path.Combine(copy, "src", "EquityBrief.Worker", "bin", "Release");

        if (!Directory.Exists(builds))
        {
            return null;
        }

        return Directory.EnumerateFiles(builds, WorkerAssembly, SearchOption.AllDirectories)
            .Select(Path.GetDirectoryName)
            .OrderBy(folder => folder, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    // The newest night's worker build, where the commit file names a commit whose copy holds one.
    public static string? NewestWorkerBuild(string dataRoot) =>
        Commit(dataRoot) is { } commit ? WorkerBuild(dataRoot, commit) : null;

    // The refusal the script last wrote, or none where the file is absent or holds no instant.
    public static NightRefusal? Refusal(string dataRoot)
    {
        var file = Path.Combine(dataRoot, RefusalFileName);

        try
        {
            if (!File.Exists(file))
            {
                return null;
            }

            var line = File.ReadAllText(file).Trim();
            var cut = line.IndexOf(' ', StringComparison.Ordinal);

            if (cut <= 0
                || !DateTimeOffset.TryParseExact(line[..cut], "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
            {
                return null;
            }

            return new NightRefusal(at, line[(cut + 1)..].Trim());
        }
        catch (IOException)
        {
            return null;
        }
    }
}
