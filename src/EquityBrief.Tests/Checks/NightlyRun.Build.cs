using EquityBrief.Core.Configuration;
using EquityBrief.Worker;

namespace EquityBrief.Tests.Checks;

// The night records the commit the script built it from, reads its secrets file by the path the script
// names, and reads the files the script leaves under the data root as the script writes them.
// see: Each night is built from a clean copy of the main checkout's own commit and never from its working tree, and refuses only a checkout off main or ahead of the remote's main
public partial class NightlyRun
{
    // The rows a store holds under the build stage: the run id, the outcome and the detail of each.
    static IReadOnlyList<string[]> BuildRows(TemporaryStore store)
    {
        using var connection = store.Open();
        using var command = connection.CreateCommand();

        command.CommandText = "SELECT run_id, outcome, detail FROM run_log WHERE stage = $stage ORDER BY rowid;";
        command.Parameters.AddWithValue("$stage", NightBuild.Stage);

        var rows = new List<string[]>();

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            rows.Add([reader.GetString(0), reader.GetString(1), reader.GetString(2)]);
        }

        return rows;
    }

    [Fact]
    public async Task TheNightRecordsTheCommitItWasBuiltFromUnderItsOwnStageAndANightBuiltByNobodyRecordsNone()
    {
        const string note = "built from 8ae51b59cb91, 2 commit(s) behind origin/main, built in 94 s";

        using var store = new TemporaryStore();

        var (code, output, error) = await NightAsync(store, build: new Nightly.Build("8ae51b59cb91", note));

        Assert.True(code == 0, error);

        // One row under the build stage, written as the first try's own, holding the line the script gave,
        // and the migrate step's own line says it too.
        var rows = BuildRows(store);
        var row = Assert.Single(rows);

        Assert.StartsWith("night-", row[0], StringComparison.Ordinal);
        Assert.Equal("ok", row[1]);
        Assert.Equal(note, row[2]);
        Assert.Contains("  migrate: applied ", output, StringComparison.Ordinal);
        Assert.Contains(", " + note, output, StringComparison.Ordinal);

        // A night handed no build, which is the suite's and a person's from a build of their own, records none.
        using var bare = new TemporaryStore();

        var (again, _, said) = await NightAsync(bare);

        Assert.True(again == 0, said);
        Assert.Empty(BuildRows(bare));

        // A note not opening with the commit is prefixed with it, so the record always names the commit first.
        Assert.Equal("built from abc123def456, build reused", new Nightly.Build("abc123def456", "build reused").Detail);
    }

    [Fact]
    public void TheWorkersSecretsFileIsReadByThePathTheEnvironmentNamesBeforeTheEnvironmentAndAnAbsentOneIsNoFault()
    {
        using var root = new TemporaryDirectory();

        var build = Path.Combine(root.Path, "build");
        var checkout = Path.Combine(root.Path, "checkout");

        Directory.CreateDirectory(build);
        Directory.CreateDirectory(checkout);
        File.WriteAllText(Path.Combine(build, "appsettings.json"), "{\"EquityBrief\":{\"DataRoot\":\"data\",\"Word\":\"from the settings\"}}");

        var secrets = Path.Combine(checkout, "appsettings.Secrets.json");

        File.WriteAllText(secrets, "{\"EquityBrief\":{\"Word\":\"from the main checkout\",\"Key\":\"the key\"}}");

        // The named file is read, over the settings beside the build, and nothing is copied beside the build.
        // A variable in the environment still wins over the named file, read under a key of this test's own so
        // no other test, and no gate's script, sets it.
        Environment.SetEnvironmentVariable("EquityBrief__SuiteWord", "from the environment");

        try
        {
            File.WriteAllText(secrets, "{\"EquityBrief\":{\"Word\":\"from the main checkout\",\"Key\":\"the key\",\"SuiteWord\":\"from the main checkout\"}}");

            var configuration = WorkerConfiguration.Build(build, secrets);

            Assert.Equal("from the main checkout", configuration["EquityBrief:Word"]);
            Assert.Equal("the key", configuration["EquityBrief:Key"]);
            Assert.Equal("from the environment", configuration["EquityBrief:SuiteWord"]);
            Assert.False(File.Exists(Path.Combine(build, "appsettings.Secrets.json")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("EquityBrief__SuiteWord", null);
        }

        // None named, or one named that is not there: the settings alone, and no fault.
        Assert.Equal("from the settings", WorkerConfiguration.Build(build, null)["EquityBrief:Word"]);
        Assert.Equal("from the settings", WorkerConfiguration.Build(build, Path.Combine(checkout, "no-such-file.json"))["EquityBrief:Word"]);
        Assert.Null(WorkerConfiguration.Build(build, null)["EquityBrief:Key"]);
    }

    [Fact]
    public void TheFilesTheScriptLeavesUnderTheDataRootAreReadAsItWritesThem()
    {
        using var root = new TemporaryDirectory();

        var data = Path.Combine(root.Path, "data-root");

        Directory.CreateDirectory(data);

        // Nothing written: no commit, no build, no refusal.
        Assert.Null(NightBuild.Commit(data));
        Assert.Null(NightBuild.NewestWorkerBuild(data));
        Assert.Null(NightBuild.Refusal(data));

        // The refusal: the instant, a space and the reason; a line with no instant is no refusal.
        File.WriteAllText(Path.Combine(data, NightBuild.RefusalFileName), "2026-10-01T23:30:05Z the checkout is on 'other' and not on main, so no night was built from it\n");

        var refusal = NightBuild.Refusal(data);

        Assert.NotNull(refusal);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 23, 30, 5, TimeSpan.Zero), refusal.At);
        Assert.Equal("the checkout is on 'other' and not on main, so no night was built from it", refusal.Reason);

        File.WriteAllText(Path.Combine(data, NightBuild.RefusalFileName), "refused for no stated instant\n");

        Assert.Null(NightBuild.Refusal(data));

        // The commit, trimmed, and its copy's worker build found by the assembly under its Release folder,
        // whatever framework folder the build wrote it in.
        const string commit = "8ae51b59cb91f00ba11ce0123456789abcdef012";

        File.WriteAllText(Path.Combine(data, NightBuild.CommitFileName), commit + "\n");

        Assert.Equal(commit, NightBuild.Commit(data));
        Assert.Null(NightBuild.WorkerBuild(data, commit));
        Assert.Null(NightBuild.NewestWorkerBuild(data));

        var build = Path.Combine(data, NightBuild.CopiesFolder, commit[..12], "src", "EquityBrief.Worker", "bin", "Release", Repository.Framework);

        Directory.CreateDirectory(build);
        File.WriteAllText(Path.Combine(build, NightBuild.WorkerAssembly), "the night's worker");

        Assert.Equal(build, NightBuild.WorkerBuild(data, commit));
        Assert.Equal(build, NightBuild.WorkerBuild(data, commit[..12]));
        Assert.Equal(build, NightBuild.NewestWorkerBuild(data));
    }
}
