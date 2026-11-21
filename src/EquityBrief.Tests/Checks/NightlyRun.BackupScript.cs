using EquityBrief.Core.Configuration;

namespace EquityBrief.Tests.Checks;

// nightly-run, the operator's word of 2026-11-20: tools/backup copies the store as a run of its own, from the build the
// newest night made, the copy night.build names in the data root's folder of nights, so it builds nothing and removes
// nothing; a data root holding no night's build is refused before any worker exists. Read in its check mode over a
// temporary repository as the off-peak run's script is.
// see: The store is copied by a scheduled run of its own holding the drain's lock for the copy alone, and the oldest beyond two is removed once the new copy is opened and read
public partial class NightlyRun
{
    [Fact]
    public void TheBackupScriptRunsTheNightsOwnBuildAndRefusesADataRootHoldingNone()
    {
        if (Tools() is not var (git, bash))
        {
            return;
        }

        using var checkout = new Checkout(git, bash);

        // No night has built: refused on stderr before any worker exists.
        var none = checkout.CheckWith("backup", null, []);

        Assert.Equal(1, none.ExitCode);
        Assert.Contains("backup: refused before any worker exists:", none.StandardError, StringComparison.Ordinal);
        Assert.Contains("night.build names no night's build", none.StandardError, StringComparison.Ordinal);
        Assert.Empty(none.StandardOutput.Trim());

        // night.build names the first commit, whose copy holds no worker yet: refused, naming the copy.
        Directory.CreateDirectory(checkout.Data);
        File.WriteAllText(Path.Combine(checkout.Data, "night.build"), checkout.First + "\n");

        var unbuilt = checkout.CheckWith("backup", null, []);

        Assert.Equal(1, unbuilt.ExitCode);
        Assert.Contains($"the night's build of {Short(checkout.First)} holds no worker assembly", unbuilt.StandardError, StringComparison.Ordinal);

        // With the night's worker in its copy: the run would start that worker over the checkout's data root, the
        // secrets named from the checkout and nothing copied, and nothing is built or removed.
        var build = Path.Combine(checkout.Data, NightBuild.CopiesFolder, Short(checkout.First), "src", "EquityBrief.Worker", "bin", "Release", Repository.Framework);

        Directory.CreateDirectory(build);
        File.WriteAllText(Path.Combine(build, NightBuild.WorkerAssembly), "a worker the night built");

        var other = Path.Combine(checkout.Data, NightBuild.CopiesFolder, "000000000000");

        Directory.CreateDirectory(other);

        var built = checkout.CheckWith("backup", null, []);

        Assert.True(built.ExitCode == 0, built.Output);
        AssertNames("backup: would run " + Path.Combine(build, NightBuild.WorkerAssembly), built.StandardOutput);
        AssertNames("backup: data root " + checkout.Data, built.StandardOutput);
        AssertNames("backup: secrets read from " + Path.Combine(checkout.Root, "src", "EquityBrief.Worker", "appsettings.Secrets.json") + ", none copied", built.StandardOutput);
        Assert.Contains("backup: would copy the store, holding the drain's lock for the copy alone", built.StandardOutput, StringComparison.Ordinal);
        Assert.True(Directory.Exists(other));
        Assert.False(Directory.Exists(Path.Combine(checkout.Data, NightBuild.CopiesFolder, Short(checkout.First) + ".partial")));

        // An argument the script does not take is refused.
        var unknown = checkout.CheckWith("backup", null, "--now");

        Assert.Equal(1, unknown.ExitCode);
        Assert.Contains("backup: '--now' is not an argument this script takes", unknown.StandardError, StringComparison.Ordinal);
    }
}
