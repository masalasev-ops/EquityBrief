using System.Globalization;

namespace EquityBrief.Core.Sweep;

// Where the sweep keeps its saved chunks and its report: a folder the settings name, or one beside the store,
// and inside it one folder a run, named by the instant the run started, with nothing at the root. The worker
// writes there and the read surface serves the reports from there, so both resolve it here.
public static class SweepFolder
{
    public const string Key = "EquityBrief:Sweep:Folder";

    // The folder's name beside the store, and the name of the chunks' folder inside it.
    public const string Name = "sweep";

    public const string CandidatesFolder = "candidates";

    public const string ReportFile = "report.html";

    // The route the read surface serves the newest report on, and a named run's under it.
    public const string Route = "/sweep";

    public static string Resolve(string? configured, string dataRoot) =>
        configured is { Length: > 0 } named ? named : Path.Combine(dataRoot, Name);

    // A run's folder name, the instant it started in UTC, which sorts by time and holds no character a folder
    // name refuses on either platform.
    public static string RunName(DateTimeOffset started) => started.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    public static bool IsRunName(string name) =>
        name.Length == 16 && DateTimeOffset.TryParseExact(name, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _);

    // The runs the folder holds, newest first, each the name of a folder under it that is a run's.
    public static IReadOnlyList<string> Runs(string folder) =>
        Directory.Exists(folder)
            ? [.. Directory.GetDirectories(folder).Select(Path.GetFileName).Where(name => name is not null && IsRunName(name)).Select(name => name!).OrderDescending(StringComparer.Ordinal)]
            : [];

    // The report served on the route: the newest run's where one holds a report, and none otherwise, since a
    // file at the root is no run's.
    public static string? NewestReport(string folder)
    {
        foreach (var run in Runs(folder))
        {
            var report = Path.Combine(folder, run, ReportFile);

            if (File.Exists(report))
            {
                return report;
            }
        }

        return null;
    }
}
