using System.Diagnostics;
using System.Globalization;
using System.Text;
using EquityBrief.Core.Cloudflare;
using EquityBrief.Core.Providers;

namespace EquityBrief.Worker.Cloudflare;

// The worker's publish-site verb, which the operator runs by hand and the scheduler after the off-peak run each night. It
// writes the read-only copy into the data root's own folder for it, removed whole first, by starting the read surface's
// build beside its own with the copy's verb, which asks no provider and writes nothing to the store; reads the folder with
// the copy's check; writes the report beside the folder and prints it; and only with the upload flag, a check that refused
// nothing and the API token and account the secrets file holds, uploads that folder alone to Cloudflare Pages. The token
// is read from the configuration, handed to the host in a header and printed nowhere.
// see: The read-only copy is published to Cloudflare Pages with no control and no part of the method, and its upload refuses a page that leaks one
public sealed class SitePublisher(
    string dataRoot,
    string workerBuild,
    IReadOnlyList<string> secretsFiles,
    IReadOnlyList<string> roots,
    Func<ProcessStartInfo, TimeSpan, (int ExitCode, IReadOnlyList<string> Lines)>? run = null,
    Func<string, string, ICloudflarePagesFeed>? feed = null,
    EquityBrief.Core.Time.IClock? clock = null,
    Func<TimeSpan, Task>? wait = null)
{
    // How long the read surface may take to write the copy before it is stopped.
    public static readonly TimeSpan CopyTakesAtMost = TimeSpan.FromHours(2);

    // From the operator's word of 2026-11-17 an upload the network failed is tried again after each of these waits, four
    // tries over half an hour, and an upload Cloudflare answered and refused is not.
    // see: The read-only copy states no multiple of the risk and retries an upload only the network failed, and Home says when it was last uploaded
    public static readonly IReadOnlyList<TimeSpan> UploadWaits = [TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(15)];

    public static int UploadTries => UploadWaits.Count + 1;

    readonly EquityBrief.Core.Time.IClock time = clock ?? EquityBrief.Core.Time.SystemClock.ForUnitedStatesSessions();

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

        // From 22.17 a copy the read surface found the method in is read as every copy is, and its upload refused.
        var leaked = exit == PublishedSite.Leaked;

        // From the operator's correction of 2026-11-26 a copy standing apart from the app is read as every copy is, and its
        // upload refused with each fault the read surface named.
        // see: The read-only copy draws each page as the app draws it, and a link it does not hold keeps its element
        var unlike = lines.Where(line => line.StartsWith(PublishedSite.UnlikeLine, StringComparison.Ordinal)).Select(line => line[PublishedSite.UnlikeLine.Length..]).ToArray();

        if ((exit != 0 && !leaked && exit != PublishedSite.Unlike) || !Directory.Exists(folder))
        {
            Say("refused: the read surface did not write the copy, so nothing was uploaded.");
            Write(report);
            Attempted(upload, "the copy could not be written, so nothing was sent");

            return 1;
        }

        var read = SiteChecks.Read(folder, SiteChecks.Needles(secretsFiles, roots, dataRoot));
        var check = read with { Refusals = [.. read.Refusals, .. leaked ? [SiteChecks.LeakedTheMethod] : Array.Empty<string>(), .. unlike.Select(fault => SiteChecks.UnlikeTheApp + fault)] };

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

            var pages = lines
                .Select(line => System.Text.RegularExpressions.Regex.Match(line, "leaks the method: (?<page>.+?) carries "))
                .Where(match => match.Success)
                .Select(match => match.Groups["page"].Value)
                .Distinct(StringComparer.Ordinal)
                .Count();

            Attempted(upload, leaked && check.Refusals.Count == 1
                ? FormattableString.Invariant($"the check found words of the method on {pages} {(pages == 1 ? "page" : "pages")} of the copy, so nothing was sent")
                : FormattableString.Invariant($"the check refused the copy for {check.Refusals.Count} {(check.Refusals.Count == 1 ? "reason" : "reasons")}, so nothing was sent"));
        }
        else if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(account))
        {
            Say($"Nothing was uploaded: {(string.IsNullOrWhiteSpace(token) ? PublishedSite.TokenKey : PublishedSite.AccountKey)} is not set in the secrets file.");
            Attempted(upload, "the Cloudflare token or account is not set, so nothing was sent");
        }
        else
        {
            var pages = feed?.Invoke(token, account) ?? CloudflarePagesFeed.Live(token, account, PublishedSite.Project);
            var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Where(file => Path.GetRelativePath(folder, file) != PublishedSite.HeadersFile)
                .Select(file => PagesFiles.Of(folder, file))
                .ToArray();
            var headers = File.ReadAllText(Path.Combine(folder, PublishedSite.HeadersFile));

            // Only the upload is tried again, the copy and its check never: a try the network failed waits and goes again, and
            // any other failure ends the run.
            for (var tried = 1; ; tried++)
            {
                try
                {
                    var deployed = await pages.DeployAsync(files, headers, Say);

                    Say($"Uploaded to the Cloudflare Pages project '{PublishedSite.Project}': {deployed}");
                    SiteUpload.Succeeded(time.UtcNow, PublishedSite.NightOf(lines), deployed).Write(dataRoot);

                    break;
                }
                catch (Exception failed) when (Unreached(failed) && tried < UploadTries)
                {
                    var waiting = UploadWaits[tried - 1];

                    Say(FormattableString.Invariant($"The upload's try {tried} of {UploadTries} failed: {pages.Scrubbed(failed.Message)}; trying again in {waiting.TotalMinutes:0} minutes."));
                    await (wait ?? Task.Delay)(waiting);
                }
                catch (Exception failed) when (failed is InvalidOperationException or TaskCanceledException or IOException)
                {
                    var said = pages.Scrubbed(failed.Message);

                    Say(Unreached(failed)
                        ? FormattableString.Invariant($"The upload failed: {said}, on its last try of {UploadTries}.")
                        : "The upload failed: " + said);
                    Attempted(upload, Unreached(failed)
                        ? FormattableString.Invariant($"Cloudflare could not be reached on any of {UploadTries} tries over {UploadWaits.Sum(one => one.TotalMinutes):0} minutes")
                        : "Cloudflare refused the upload: " + said);
                    outcome = 1;

                    break;
                }
            }
        }

        Write(report);

        return outcome;
    }

    // A failure the network caused before Cloudflare answered: the host not reached, no answer in time, or the connection lost.
    static bool Unreached(Exception failed) => failed is PagesUnreachable or TaskCanceledException or IOException;

    // An attempt the run was asked to make and that sent nothing, written over the record with the last upload kept.
    void Attempted(bool upload, string why)
    {
        if (upload)
        {
            SiteUpload.FailedAfter(SiteUpload.Read(dataRoot), time.UtcNow, why).Write(dataRoot);
        }
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
