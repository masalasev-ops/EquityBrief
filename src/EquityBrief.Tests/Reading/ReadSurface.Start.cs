using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Api.Passes;
using EquityBrief.Core.Configuration;
using EquityBrief.Data.Migrations;
using EquityBrief.Tests.Checks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace EquityBrief.Tests.Reading;

// How the read surface is started: the command the runbook gives, the settings it reads, and
// the store those settings put it on.
public partial class ReadSurface
{
    [Fact]
    public async Task TheSurfaceReadsTheStoreAndTheReportUnderItsCheckoutWhereverItIsStarted()
    {
        // The surface's own build in a checkout of its own: a directory holding the solution
        // file, a data folder with a store in it, a phase report where the harness writes one,
        // and the build where a checkout keeps it. Started from a directory outside it, with
        // nothing in the environment naming a store, it writes its start row as it comes up and
        // before it serves anything, so the row landing in the checkout's store is the surface
        // having found it, and the run page states the checkout's report.
        using var checkout = new TemporaryDirectory();
        using var elsewhere = new TemporaryDirectory();

        var built = Path.GetDirectoryName(Repository.BuildOutput("EquityBrief.Api", "EquityBrief.Api.dll"))!;
        var copy = Path.Combine(checkout.Path, Path.GetRelativePath(Repository.Root, built));

        Assert.True(Directory.Exists(built), $"No read surface build at {built}.");

        foreach (var file in Directory.GetFiles(built, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(copy, Path.GetRelativePath(built, file));

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        File.WriteAllText(Path.Combine(checkout.Path, Checkout.SolutionFile), "<Solution />");

        var database = Path.Combine(checkout.Path, "data", StoreLocation.DatabaseFileName);

        Directory.CreateDirectory(Path.GetDirectoryName(database)!);
        MigrationRunner.Standard().Apply(database);

        var report = Path.Combine(checkout.Path, "artifacts", "phase-report.json");

        Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        File.WriteAllText(report, """{ "summary": { "pass": 7, "fail": 0, "unexamined": 0, "outOfScope": 0 } }""");

        var dotnet = Shell.Locate("dotnet");

        Assert.NotNull(dotnet);

        var start = new ProcessStartInfo(dotnet!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = elsewhere.Path,
        };

        start.ArgumentList.Add(Path.Combine(copy, "EquityBrief.Api.dll"));
        start.ArgumentList.Add("--urls");
        start.ArgumentList.Add("http://127.0.0.1:0");

        // The suite's own scripts name a store of their own in the environment, which a child
        // inherits, and that would be the store the surface opened rather than the one its
        // settings name.
        start.Environment.Remove(StoreLocation.DataRootKey.Replace(":", "__", StringComparison.Ordinal));
        start.Environment.Remove("ASPNETCORE_ENVIRONMENT");

        var said = new StringBuilder();
        var listening = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var process = Process.Start(start)!;

        process.ErrorDataReceived += (_, line) => { lock (said) { said.AppendLine(line.Data); } };
        process.OutputDataReceived += (_, line) =>
        {
            if (line.Data is { } text && Regex.Match(text, @"Now listening on: (?<address>http://\S+)") is { Success: true } found)
            {
                listening.TrySetResult(found.Groups["address"].Value);
            }
        };
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        try
        {
            var opened = false;
            var watch = Stopwatch.StartNew();

            while (!opened && !process.HasExited && watch.Elapsed < TimeSpan.FromSeconds(60))
            {
                await Task.Delay(250);
                opened = StartRows(database) > 0;
            }

            opened = opened || StartRows(database) > 0;

            Assert.True(
                opened,
                $"No read-api row in the checkout's store after {watch.Elapsed.TotalSeconds:0} s, the surface "
                + $"{(process.HasExited ? "having exited" : "still running")}: {said}");

            var address = await listening.Task.WaitAsync(TimeSpan.FromSeconds(60));

            using var client = new HttpClient { BaseAddress = new Uri(address) };

            var run = await client.GetStringAsync("/screens/run/2026-09-17");

            Assert.Contains("class=\"harness\" data-passed=\"7\"", run, StringComparison.Ordinal);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync();

            using (var connection = new SqliteConnection(MigrationRunner.ConnectionStringFor(database)))
            {
                SqliteConnection.ClearPool(connection);
            }
        }
    }

    static long StartRows(string database)
    {
        using var connection = new SqliteConnection(MigrationRunner.ConnectionStringFor(database));
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM run_log WHERE stage = 'read-api';";

        return (long)command.ExecuteScalar()!;
    }

    [Fact]
    public void TheSurfaceNamesTheStoreTheWorkerWritesAndTheRunbookStartsItAtItsOwnAddress()
    {
        // The surface's settings name the data root the worker's do, so the screens read the
        // store the night writes, and the runbook gives the command and the address the launch
        // settings serve it at, both read from those files rather than restated here.
        static string? DataRoot(string project)
        {
            using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repository.Root, "src", project, "appsettings.json")));

            return settings.RootElement.TryGetProperty("EquityBrief", out var section)
                && section.TryGetProperty("DataRoot", out var root)
                    ? root.GetString()
                    : null;
        }

        Assert.NotNull(DataRoot("EquityBrief.Api"));
        Assert.Equal(DataRoot("EquityBrief.Worker"), DataRoot("EquityBrief.Api"));

        using var launch = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(Repository.Root, "src", "EquityBrief.Api", "Properties", "launchSettings.json")));

        var address = launch.RootElement.GetProperty("profiles").EnumerateObject().First().Value
            .GetProperty("applicationUrl").GetString()!
            .Split(';')
            .Single(url => url.StartsWith("http://", StringComparison.Ordinal));

        var runbook = Corpus.Read("docs/RUNBOOK.md");

        Assert.Matches(new Regex(@"^## Opening the screens$", RegexOptions.Multiline), runbook);
        Assert.Contains("dotnet run --project src/EquityBrief.Api", runbook, StringComparison.Ordinal);
        Assert.Contains("`" + address + "/`", runbook, StringComparison.Ordinal);
    }

    [Fact]
    public void ARelativeDataRootSitsUnderTheCheckoutAndAnAbsoluteOneStandsAsConfigured()
    {
        // What the surface reads its settings' root against, both ways, and the refusal the
        // constructor makes of none, which the read against a directory keeps.
        var checkout = Path.Combine(Path.GetTempPath(), "some-checkout");
        var absolute = Path.Combine(Path.GetTempPath(), "some-store");

        Assert.Equal(Path.Combine(checkout, "data"), StoreLocation.Within(checkout, "data").DataRoot);
        Assert.Equal(absolute, StoreLocation.Within(checkout, absolute).DataRoot);
        Assert.Throws<ArgumentException>(() => StoreLocation.Within(checkout, null));
        Assert.Throws<ArgumentException>(() => StoreLocation.Within(checkout, "  "));

        // No checkout found, as a build copied out of one would have: the working directory,
        // as the constructor reads it.
        Assert.Equal(Path.GetFullPath("data"), StoreLocation.Within(null, "data").DataRoot);
    }

    [Fact]
    public void TheSuitesHostReadsTheStoreItIsHandedAndNotTheOneTheShippedSettingsName()
    {
        // The shipped settings name a data root, and every test hosting the surface hands it a
        // throwaway one. Were the shipped one to win, the suite would read the operator's store.
        using var store = new TemporaryStore().Migrated();
        using var host = new Host(store.Root);

        Assert.Equal(Path.GetFullPath(store.Root), host.Services.GetRequiredService<StoreLocation>().DataRoot);
    }
}
