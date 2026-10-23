using EquityBrief.Core.Configuration;

namespace EquityBrief.Tests.Checks;

// nightly-run, from 20.1: tools/localrun runs the local run's slots and its by-hand forms from a clean copy of main's own
// commit as the off-peak run does, into the same folder of nights, reusing a copy a night built, building main's commit
// and not the branch's where the checkout is on a branch, refusing a main holding a commit origin/main lacks before any
// worker exists, and refusing a run that names no form; read in its check mode over temporary repositories.
// see: The local run is a process of its own at two slots outside the night and refuses any model file or prompt but its pin, whatever runtime answers
// see: Each night is built from a clean copy of the main checkout's own commit and never from its working tree, and refuses only a checkout off main or ahead of the remote's main
public partial class NightlyRun
{
    [Fact]
    public void TheLocalRunsScriptRunsFromACleanCopyOfMainWhateverTheBranchAndRefusesAMainAheadOfTheRemote()
    {
        if (Tools() is not var (git, bash))
        {
            return;
        }

        using var checkout = new Checkout(git, bash);

        // Clean, on main, at origin/main: the late slot would build the checkout's own commit into the night's folder of
        // copies, with the store and the secrets named from the checkout and nothing copied, and run the worker's form.
        var clean = checkout.CheckWith("localrun", null, "--slot", "late");

        Assert.True(clean.ExitCode == 0, clean.Output);
        Assert.Contains($"localrun: would run {checkout.First}, built from {Short(checkout.First)}", clean.StandardOutput, StringComparison.Ordinal);
        AssertNames(Path.Combine(checkout.Data, NightBuild.CopiesFolder, Short(checkout.First)) + ", to build", clean.StandardOutput);
        AssertNames("localrun: data root " + checkout.Data, clean.StandardOutput);
        AssertNames("localrun: secrets read from " + Path.Combine(checkout.Root, "src", "EquityBrief.Worker", "appsettings.Secrets.json") + ", none copied", clean.StandardOutput);
        Assert.Contains("localrun: would run local-run --slot late", clean.StandardOutput, StringComparison.Ordinal);

        // A copy a night built of the same commit is run as it is, for the pin as for a slot.
        var build = Path.Combine(checkout.Data, NightBuild.CopiesFolder, Short(checkout.First), "src", "EquityBrief.Worker", "bin", "Release", Repository.Framework);

        Directory.CreateDirectory(build);
        File.WriteAllText(Path.Combine(build, NightBuild.WorkerAssembly), "a worker built already");

        var reused = checkout.CheckWith("localrun", null, "--pin");

        Assert.True(reused.ExitCode == 0, reused.Output);
        AssertNames(Path.Combine(checkout.Data, NightBuild.CopiesFolder, Short(checkout.First)) + ", built already", reused.StandardOutput);
        Assert.Contains("localrun: would run local-run --pin", reused.StandardOutput, StringComparison.Ordinal);

        // No form named: refused before anything is read.
        var none = checkout.CheckWith("localrun", null);

        Assert.Equal(1, none.ExitCode);
        Assert.Contains("localrun: name a form, --slot early, --slot late, --pin or --twice", none.StandardError, StringComparison.Ordinal);

        // On a branch holding a commit of its own: main's commit is built and the branch's is not, the line naming the
        // branch.
        checkout.Run("checkout", "-q", "-b", "other");
        var branchCommit = checkout.Commit("branch");

        var onBranch = checkout.CheckWith("localrun", null, "--slot", "early");

        Assert.True(onBranch.ExitCode == 0, onBranch.Output);
        Assert.Contains($"localrun: would run {checkout.First}, built from {Short(checkout.First)}, main's own commit while the checkout is on 'other'", onBranch.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain(Short(branchCommit), onBranch.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(string.Empty, onBranch.StandardError.Trim());

        // Main ahead of origin/main: refused before any worker exists, on stderr, naming the count.
        checkout.Run("checkout", "-q", "main");
        checkout.Commit("two");

        var ahead = checkout.CheckWith("localrun", null, "--twice");

        Assert.Equal(1, ahead.ExitCode);
        Assert.Contains("localrun: refused before any worker exists: main holds 1 commit(s) origin/main does not have, so no build was made from it", ahead.StandardError, StringComparison.Ordinal);
        Assert.Equal(string.Empty, ahead.StandardOutput.Trim());
    }
}
