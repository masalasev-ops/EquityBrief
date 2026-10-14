using EquityBrief.Core.Configuration;

namespace EquityBrief.Tests.Checks;

// nightly-run, from the 11.4 correction that leaves the night's report requests queued: tools/offpeak labels the newest
// night's list and drains the queued reports from a clean copy of main's own commit as the night is built, into the
// same folder of nights, reusing a copy a night built, building main's commit and not the branch's where the checkout
// is on a branch, and refusing a main holding a commit origin/main lacks before any worker exists; read in its check
// mode over temporary repositories as the monthly run's script is.
// see: The night queues its report requests and starts neither the drain nor the labeller, and the off-peak run starts both five minutes after each peak window ends
// see: Each night is built from a clean copy of the main checkout's own commit and never from its working tree, and refuses only a checkout off main or ahead of the remote's main
public partial class NightlyRun
{
    [Fact]
    public void TheOffPeakScriptRunsFromACleanCopyOfMainWhateverTheBranchReusesANightsCopyAndRefusesAMainAheadOfTheRemote()
    {
        if (Tools() is not var (git, bash))
        {
            return;
        }

        using var checkout = new Checkout(git, bash);

        // Clean, on main, at origin/main: the run would build the checkout's own commit into the night's folder of
        // copies, with the store and the secrets named from the checkout and nothing copied, and then label and drain.
        var clean = checkout.CheckWith("offpeak", null, []);

        Assert.True(clean.ExitCode == 0, clean.Output);
        Assert.Contains($"offpeak: would run {checkout.First}, built from {Short(checkout.First)}", clean.StandardOutput, StringComparison.Ordinal);
        AssertNames(Path.Combine(checkout.Data, NightBuild.CopiesFolder, Short(checkout.First)) + ", to build", clean.StandardOutput);
        AssertNames("offpeak: data root " + checkout.Data, clean.StandardOutput);
        AssertNames("offpeak: secrets read from " + Path.Combine(checkout.Root, "src", "EquityBrief.Worker", "appsettings.Secrets.json") + ", none copied", clean.StandardOutput);
        Assert.Contains("offpeak: would label the newest night's list and then drain the queued reports", clean.StandardOutput, StringComparison.Ordinal);

        // A copy a night built of the same commit is run as it is.
        var build = Path.Combine(checkout.Data, NightBuild.CopiesFolder, Short(checkout.First), "src", "EquityBrief.Worker", "bin", "Release", Repository.Framework);

        Directory.CreateDirectory(build);
        File.WriteAllText(Path.Combine(build, NightBuild.WorkerAssembly), "a worker built already");

        var reused = checkout.CheckWith("offpeak", null, []);

        Assert.True(reused.ExitCode == 0, reused.Output);
        AssertNames(Path.Combine(checkout.Data, NightBuild.CopiesFolder, Short(checkout.First)) + ", built already", reused.StandardOutput);

        // On a branch holding a commit of its own: main's commit is built and the branch's is not, the line naming the
        // branch, since a session works on a branch through the day the run starts in.
        checkout.Run("checkout", "-q", "-b", "other");
        var branchCommit = checkout.Commit("branch");

        var onBranch = checkout.CheckWith("offpeak", null, []);

        Assert.True(onBranch.ExitCode == 0, onBranch.Output);
        Assert.Contains($"offpeak: would run {checkout.First}, built from {Short(checkout.First)}, main's own commit while the checkout is on 'other'", onBranch.StandardOutput, StringComparison.Ordinal);
        AssertNames(Path.Combine(checkout.Data, NightBuild.CopiesFolder, Short(checkout.First)) + ", built already", onBranch.StandardOutput);
        Assert.DoesNotContain(Short(branchCommit), onBranch.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(string.Empty, onBranch.StandardError.Trim());

        // Main ahead of origin/main: refused before any worker exists, on stderr, naming the count, whichever branch
        // the checkout is on, the refusal reading main and not the checkout's own commit.
        checkout.Run("checkout", "-q", "main");
        checkout.Commit("two");

        var ahead = checkout.CheckWith("offpeak", null, []);

        Assert.Equal(1, ahead.ExitCode);
        Assert.Contains("offpeak: refused before any worker exists: main holds 1 commit(s) origin/main does not have, so no build was made from it", ahead.StandardError, StringComparison.Ordinal);
        Assert.Equal(string.Empty, ahead.StandardOutput.Trim());

        checkout.Run("checkout", "-q", "other");

        var aheadFromBranch = checkout.CheckWith("offpeak", null, []);

        Assert.Equal(1, aheadFromBranch.ExitCode);
        Assert.Contains("main holds 1 commit(s) origin/main does not have, so no build was made from it", aheadFromBranch.StandardError, StringComparison.Ordinal);
    }
}
