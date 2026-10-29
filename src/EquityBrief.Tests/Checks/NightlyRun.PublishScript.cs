using EquityBrief.Core.Configuration;

namespace EquityBrief.Tests.Checks;

// nightly-run: tools/publish-site writes the read-only copy from a clean copy of main's own commit as the off-peak run
// does, in the same folder of nights, reusing a copy a night built and building the read surface into it beside the
// worker, building main's commit and not the branch's where the checkout is on a branch, refusing a main holding a commit
// origin/main lacks before any worker exists, and passing on the upload flag alone; read in its check mode over temporary
// repositories as the off-peak run's script is.
// see: A read-only copy of the site is published to Cloudflare Pages from the read surface's own screens with no control, a link it does not hold drawn as plain text
// see: Each night is built from a clean copy of the main checkout's own commit and never from its working tree, and refuses only a checkout off main or ahead of the remote's main
public partial class NightlyRun
{
    [Fact]
    public void ThePublishScriptWritesTheCopyFromACleanCopyOfMainBuildingTheReadSurfaceBesideTheWorker()
    {
        if (Tools() is not var (git, bash))
        {
            return;
        }

        using var checkout = new Checkout(git, bash);
        var copy = Path.Combine(checkout.Data, NightBuild.CopiesFolder, Short(checkout.First));

        // Clean, on main, at origin/main: both builds to make in the night's folder of copies, the store and the secrets
        // named from the checkout and nothing copied, and the copy written beside nothing it uploads.
        var clean = checkout.CheckWith("publish-site", null, []);

        Assert.True(clean.ExitCode == 0, clean.Output);
        Assert.Contains($"publish-site: would run {checkout.First}, built from {Short(checkout.First)}", clean.StandardOutput, StringComparison.Ordinal);
        AssertNames(copy + ", worker to build, read surface to build", clean.StandardOutput);
        AssertNames("publish-site: data root " + checkout.Data, clean.StandardOutput);
        AssertNames("publish-site: secrets read from " + Path.Combine(checkout.Root, "src", "EquityBrief.Worker", "appsettings.Secrets.json") + ", none copied", clean.StandardOutput);
        Assert.Contains("and its report beside it\n", clean.StandardOutput.ReplaceLineEndings("\n"), StringComparison.Ordinal);

        // Asked to upload, it says so, and a flag it does not take is refused.
        Assert.Contains("and its report beside it, and upload it", checkout.CheckWith("publish-site", null, ["--upload"]).StandardOutput, StringComparison.Ordinal);

        var unknown = checkout.CheckWith("publish-site", null, ["--everything"]);

        Assert.Equal(1, unknown.ExitCode);
        Assert.Contains("publish-site: '--everything' is not an argument this script takes", unknown.StandardError, StringComparison.Ordinal);

        // A copy a night built holds the worker alone, and the read surface is built into it.
        var worker = Path.Combine(copy, "src", "EquityBrief.Worker", "bin", "Release", Repository.Framework);

        Directory.CreateDirectory(worker);
        File.WriteAllText(Path.Combine(worker, NightBuild.WorkerAssembly), "a worker built already");

        AssertNames(copy + ", worker built already, read surface to build", checkout.CheckWith("publish-site", null, []).StandardOutput);

        var surface = Path.Combine(copy, "src", "EquityBrief.Api", "bin", "Release", Repository.Framework);

        Directory.CreateDirectory(surface);
        File.WriteAllText(Path.Combine(surface, "EquityBrief.Api.dll"), "a surface built already");

        AssertNames(copy + ", worker built already, read surface built already", checkout.CheckWith("publish-site", null, []).StandardOutput);

        // On a branch holding a commit of its own, main's commit is still the one built.
        checkout.Run("checkout", "-q", "-b", "other");
        var branchCommit = checkout.Commit("branch");
        var onBranch = checkout.CheckWith("publish-site", null, []);

        Assert.True(onBranch.ExitCode == 0, onBranch.Output);
        Assert.Contains($"publish-site: would run {checkout.First}, built from {Short(checkout.First)}, main's own commit while the checkout is on 'other'", onBranch.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain(Short(branchCommit), onBranch.StandardOutput, StringComparison.Ordinal);

        // Main ahead of origin/main: refused before any worker exists.
        checkout.Run("checkout", "-q", "main");
        checkout.Commit("two");

        var ahead = checkout.CheckWith("publish-site", null, []);

        Assert.Equal(1, ahead.ExitCode);
        Assert.Contains("publish-site: refused before any worker exists: main holds 1 commit(s) origin/main does not have, so no build was made from it", ahead.StandardError, StringComparison.Ordinal);
        Assert.Equal(string.Empty, ahead.StandardOutput.Trim());
    }
}
