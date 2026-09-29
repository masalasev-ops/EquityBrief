namespace EquityBrief.Core.Sweep;

// Where the sweep keeps its saved chunks and its report: a folder the settings name, or one beside the store.
// The worker writes there and the read surface serves the report from there, so both resolve it here.
public static class SweepFolder
{
    public const string Key = "EquityBrief:Sweep:Folder";

    // The folder's name beside the store, and the name of the chunks' folder inside it.
    public const string Name = "sweep";

    public const string CandidatesFolder = "candidates";

    public const string ReportFile = "report.html";

    // The route the read surface serves the report on.
    public const string Route = "/sweep";

    public static string Resolve(string? configured, string dataRoot) =>
        configured is { Length: > 0 } named ? named : Path.Combine(dataRoot, Name);
}
