using EquityBrief.Core.Configuration;

namespace EquityBrief.Tests.Checks;

// nightly-run, from the 11.4 correction that leaves the night's report requests queued: tools/offpeak labels the newest
// night's list and drains the queued reports from a clean copy of the main checkout's own commit as the night is built,
// into the same folder of nights, reusing a copy a night built, and refuses a checkout off main or holding a commit
// origin/main lacks before any worker exists; read in its check mode over temporary repositories as the monthly run's
// script is.
// see: The night queues its report requests and starts neither the drain nor the labeller, and the off-peak run starts both at 16:30Z
// see: Each night is built from a clean copy of the main checkout's own commit and never from its working tree, and refuses only a checkout off main or ahead of the remote's main
public partial class NightlyRun
{
    [Fact]
    public void TheOffPeakScriptRunsFromACleanCopyOfMainReusesANightsCopyAndRefusesACheckoutOffMain()
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

        // Off main: refused before any worker exists, on stderr, with the reason.
        checkout.Run("checkout", "-q", "-b", "other");

        var offMain = checkout.CheckWith("offpeak", null, []);

        Assert.Equal(1, offMain.ExitCode);
        Assert.Contains("offpeak: refused before any worker exists: the checkout is on 'other' and not on main, so no build was made from it", offMain.StandardError, StringComparison.Ordinal);
        Assert.Equal(string.Empty, offMain.StandardOutput.Trim());

        // Ahead of origin/main: refused the same way, naming the count.
        checkout.Run("checkout", "-q", "main");
        checkout.Commit("two");

        var ahead = checkout.CheckWith("offpeak", null, []);

        Assert.Equal(1, ahead.ExitCode);
        Assert.Contains("the checkout holds 1 commit(s) origin/main does not have, so no build was made from it", ahead.StandardError, StringComparison.Ordinal);
    }
}
