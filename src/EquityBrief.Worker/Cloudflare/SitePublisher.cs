using System.Diagnostics;
using System.Globalization;
using System.Text;
using EquityBrief.Core.Cloudflare;

namespace EquityBrief.Worker.Cloudflare;

// The worker's publish-site verb, which the operator runs by hand and the scheduler after the off-peak run each night. It
// writes the read-only copy into the data root's own folder for it, removed whole first, by starting the read surface's
// build beside its own with the copy's verb, which asks no provider and writes nothing to the store; reads the folder with
// the copy's check; writes the report beside the folder and prints it; and only with the upload flag, a check that refused
// nothing and the API token and account the secrets file holds, uploads that folder alone to Cloudflare Pages. The token
// is read from the configuration, handed to the host in a header and printed nowhere.
// see: A read-only copy of the site is published to Cloudflare Pages from the read surface's own screens and holds no control
public sealed class SitePublisher(
    string dataRoot,
    string workerBuild,
    IReadOnlyList<string> secretsFiles,
    IReadOnlyList<string> roots,
    Func<ProcessStartInfo, TimeSpan, (int ExitCode, IReadOnlyList<string> Lines)>? run = null,
    Func<HttpClient>? http = null)
{
    // How long the read surface may take to write the copy before it is stopped.
    public static readonly TimeSpan CopyTakesAtMost = TimeSpan.FromHours(2);

    const string SurfaceProject = "EquityBrief.Api";
    const string WorkerProject = "EquityBrief.Worker";
    const string SurfaceAssembly = "EquityBrief.Api.dll";

    public static string FolderIn(string dataRoot) => Path.Combine(dataRoot, PublishedSite.Folder);

    public static string ReportIn(string dataRoot) => Path.Combine(dataRoot, PublishedSite.ReportFile);

    // The read surface's build beside the worker's: the same configuration and framework under the surface's project, as the
    // surface finds the worker's beside its own; none where the worker is not running from its own project's build.
    public static string? SurfaceBuildBeside(string build)
    {
        for (var directory = new DirectoryInfo(Path.TrimEndingDirectorySeparator(build)); directory?.Parent is { } parent; directory = parent)
        {
            if (directory.Name == WorkerProject && parent.Name == "src")
            {
                var relative = Path.GetRelativePath(directory.FullName, build);

                return relative == "." ? null : Path.Combine(parent.FullName, SurfaceProject, relative);
            }
        }

        return null;
    }

    public async Task<int> RunAsync(bool upload, string? token, string? account, Action<string> say)
    {
        var report = new List<string>();

        void Say(string line)
        {
            report.Add(line);
            say(line);
        }

        var folder = FolderIn(dataRoot);

        if (SurfaceBuildBeside(workerBuild) is not { } surfaceBuild || !File.Exists(Path.Combine(surfaceBuild, SurfaceAssembly)))
        {
            Say("refused: no read surface is built beside this worker, so no copy was written; build the read surface's project in the same configuration first.");

            return 1;
        }

        // The folder's own rewrite: removed whole and written again, under the data root and never the store.
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = surfaceBuild,
        };

        start.ArgumentList.Add(Path.Combine(surfaceBuild, SurfaceAssembly));
        start.ArgumentList.Add(PublishedSite.Verb);
        start.ArgumentList.Add(folder);
        start.Environment[EquityBrief.Core.Configuration.StoreLocation.DataRootKey.Replace(":", "__", StringComparison.Ordinal)] = dataRoot;

        var started = Stopwatch.StartNew();
        var (exit, lines) = (run ?? Run)(start, CopyTakesAtMost);

        Say(FormattableString.Invariant($"The read surface wrote the copy into {PublishedSite.Folder}/ under the data root in {started.Elapsed.TotalMinutes:0.0} minutes, ending {exit}:"));

        foreach (var line in lines)
        {
            Say("  " + line);
        }

        if (exit != 0 || !Directory.Exists(folder))
        {
            Say("refused: the read surface did not write the copy, so nothing was uploaded.");
            Write(report);

            return 1;
        }

        var check = SiteChecks.Read(folder, SiteChecks.Needles(secretsFiles, roots, dataRoot));

        foreach (var line in check.Lines)
        {
            Say(line);
        }

        Say(check.Refusals.Count == 0
            ? "Nothing the check reads refuses the upload."
            : FormattableString.Invariant($"The check refuses the upload, {check.Refusals.Count} reasons:"));

        foreach (var refusal in check.Refusals)
        {
            Say("  " + refusal);
        }

        var outcome = check.Refusals.Count == 0 ? 0 : 1;

        if (!upload)
        {
            Say($"Nothing was uploaded: the copy is written and read, and {PublishedSite.Upload} uploads it once this report is read.");
        }
        else if (check.Refusals.Count > 0)
        {
            Say("Nothing was uploaded, since the check refused it.");
        }
        else if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(account))
        {
            Say($"Nothing was uploaded: {(string.IsNullOrWhiteSpace(token) ? PublishedSite.TokenKey : PublishedSite.AccountKey)} is not set in the secrets file.");
        }
        else
        {
            var pages = new CloudflarePages(http?.Invoke() ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) }, token, account, PublishedSite.Project);

            try
            {
                var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                    .Where(file => Path.GetRelativePath(folder, file) != PublishedSite.HeadersFile)
                    .Select(file => CloudflarePages.FileOf(folder, file))
                    .ToArray();
                var deployed = await pages.DeployAsync(files, File.ReadAllText(Path.Combine(folder, PublishedSite.HeadersFile)), Say);

                Say($"Uploaded to the Cloudflare Pages project '{PublishedSite.Project}': {deployed}");
            }
            catch (Exception failed) when (failed is InvalidOperationException or HttpRequestException or TaskCanceledException or IOException)
            {
                Say("The upload failed: " + pages.Scrubbed(failed.Message));
                outcome = 1;
            }
        }

        Write(report);

        return outcome;
    }

    // The report, beside the folder and never inside it, so the copy never carries it.
    void Write(IReadOnlyList<string> lines)
    {
        var report = ReportIn(dataRoot);

        File.WriteAllText(report, string.Join('\n', lines) + "\n", new UTF8Encoding(false));
    }

    // The read surface run to its end, its lines in the order printed, stopped where it runs past the bound.
    static (int ExitCode, IReadOnlyList<string> Lines) Run(ProcessStartInfo start, TimeSpan most)
    {
        var lines = new List<string>();
        using var process = new Process { StartInfo = start };

        process.OutputDataReceived += (_, line) => { if (line.Data is { } data) { lock (lines) { lines.Add(data); } } };
        process.ErrorDataReceived += (_, line) => { if (line.Data is { } data) { lock (lines) { lines.Add(data); } } };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(most))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"stopped after {most.TotalHours:0} hours"));

            return (1, lines);
        }

        process.WaitForExit();

        return (process.ExitCode, lines);
    }
}
