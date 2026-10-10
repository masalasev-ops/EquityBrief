using EquityBrief.Core.Configuration;

namespace EquityBrief.Tests.Checks;

// nightly-run, 17.10: tools/monthly runs the month's loop from a clean copy of the main checkout's own commit as the
// night is built, into the same folder of nights, reusing a copy a night built, and refuses a checkout off main or
// holding a commit origin/main lacks before any worker exists; read in its check mode over temporary repositories as
// the night's script and the ledger's build are.
// see: The monthly run puts at most one proposal a family an index to the operator, from a clean copy of main's commit on the first Saturday of the month
// see: Each night is built from a clean copy of the main checkout's own commit and never from its working tree, and refuses only a checkout off main or ahead of the remote's main
public partial class NightlyRun
{
    [Fact]
    public void TheMonthlyScriptRunsFromACleanCopyOfMainReusesANightsCopyAndRefusesACheckoutOffMain()
    {
        if (Tools() is not var (git, bash))
        {
            return;
        }

        using var checkout = new Checkout(git, bash);

        string[] month = ["--month", "2026-11"];

        // Clean, on main, at origin/main: the run would build the checkout's own commit into the night's folder of
        // copies, with the store and the secrets named from the checkout and nothing copied.
        var clean = checkout.CheckWith("monthly", null, month);

        Assert.True(clean.ExitCode == 0, clean.Output);
        Assert.Contains($"monthly: would run {checkout.First}, built from {Short(checkout.First)}", clean.StandardOutput, StringComparison.Ordinal);
        AssertNames(Path.Combine(checkout.Data, NightBuild.CopiesFolder, Short(checkout.First)) + ", to build", clean.StandardOutput);
        AssertNames("monthly: data root " + checkout.Data, clean.StandardOutput);
        AssertNames("monthly: secrets read from " + Path.Combine(checkout.Root, "src", "EquityBrief.Worker", "appsettings.Secrets.json") + ", none copied", clean.StandardOutput);

        // A copy a night built of the same commit is run as it is.
        var build = Path.Combine(checkout.Data, NightBuild.CopiesFolder, Short(checkout.First), "src", "EquityBrief.Worker", "bin", "Release", Repository.Framework);

        Directory.CreateDirectory(build);
        File.WriteAllText(Path.Combine(build, NightBuild.WorkerAssembly), "a worker built already");

        var reused = checkout.CheckWith("monthly", null, month);

        Assert.True(reused.ExitCode == 0, reused.Output);
        AssertNames(Path.Combine(checkout.Data, NightBuild.CopiesFolder, Short(checkout.First)) + ", built already", reused.StandardOutput);

        // Off main: refused before any worker exists, on stderr, with the reason.
        checkout.Run("checkout", "-q", "-b", "other");

        var offMain = checkout.CheckWith("monthly", null, month);

        Assert.Equal(1, offMain.ExitCode);
        Assert.Contains("monthly: refused before any worker exists: the checkout is on 'other' and not on main, so no build was made from it", offMain.StandardError, StringComparison.Ordinal);
        Assert.Equal(string.Empty, offMain.StandardOutput.Trim());

        // Ahead of origin/main: refused the same way, naming the count.
        checkout.Run("checkout", "-q", "main");
        checkout.Commit("two");

        var ahead = checkout.CheckWith("monthly", null, month);

        Assert.Equal(1, ahead.ExitCode);
        Assert.Contains("the checkout holds 1 commit(s) origin/main does not have, so no build was made from it", ahead.StandardError, StringComparison.Ordinal);
    }
}
