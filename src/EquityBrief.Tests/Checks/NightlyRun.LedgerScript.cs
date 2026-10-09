using EquityBrief.Core.Configuration;
using EquityBrief.Worker.Ledger;

namespace EquityBrief.Tests.Checks;

// nightly-run, 17.3: tools/ledger-build builds the setup ledger's history from a clean copy of the main checkout's own
// commit as the night is built, into the same folder of nights, reusing a copy a night built, and refuses a checkout
// off main or holding a commit origin/main lacks before any worker exists; read in its check mode over temporary
// repositories as the night's script is. And the build's own waits and its going on from the sessions not yet
// written, each worked by hand.
// see: A setup is every member-session a family's loose gates pass, and its readings are defined once and read as they stood
// see: Each night is built from a clean copy of the main checkout's own commit and never from its working tree, and refuses only a checkout off main or ahead of the remote's main
public partial class NightlyRun
{
    [Fact]
    public void TheLedgerBuildScriptBuildsFromACleanCopyOfMainReusesANightsCopyAndRefusesACheckoutOffMain()
    {
        if (Tools() is not var (git, bash))
        {
            return;
        }

        using var checkout = new Checkout(git, bash);

        string[] span = ["--index", "GSPC", "--from", "2019-01-02", "--through", "2026-10-05"];

        // Clean, on main, at origin/main: the build would run the checkout's own commit from the night's folder of
        // copies, with the store and the secrets named from the checkout and nothing copied.
        var clean = checkout.CheckWith("ledger-build", null, span);

        Assert.True(clean.ExitCode == 0, clean.Output);
        Assert.Contains($"ledger-build: would run {checkout.First}, built from {Short(checkout.First)}", clean.StandardOutput, StringComparison.Ordinal);
        AssertNames(Path.Combine(checkout.Data, NightBuild.CopiesFolder, Short(checkout.First)) + ", to build", clean.StandardOutput);
        AssertNames("ledger-build: data root " + checkout.Data, clean.StandardOutput);
        AssertNames("ledger-build: secrets read from " + Path.Combine(checkout.Root, "src", "EquityBrief.Worker", "appsettings.Secrets.json") + ", none copied", clean.StandardOutput);

        // An edit under src/ costs nothing: the committed code is what is built.
        File.WriteAllText(Path.Combine(checkout.Root, "src", "one.txt"), "edited and not committed\n");

        var dirty = checkout.CheckWith("ledger-build", null, span);

        Assert.True(dirty.ExitCode == 0, dirty.Output);
        Assert.Contains($"ledger-build: would run {checkout.First}, built from {Short(checkout.First)}", dirty.StandardOutput, StringComparison.Ordinal);

        // A copy a night built of the same commit is run as it is.
        var build = Path.Combine(checkout.Data, NightBuild.CopiesFolder, Short(checkout.First), "src", "EquityBrief.Worker", "bin", "Release", Repository.Framework);

        Directory.CreateDirectory(build);
        File.WriteAllText(Path.Combine(build, NightBuild.WorkerAssembly), "a worker built already");

        var reused = checkout.CheckWith("ledger-build", null, span);

        Assert.True(reused.ExitCode == 0, reused.Output);
        AssertNames(Path.Combine(checkout.Data, NightBuild.CopiesFolder, Short(checkout.First)) + ", built already", reused.StandardOutput);

        // Off main: refused before any worker exists, on stderr, with the reason.
        checkout.Run("checkout", "-q", "-b", "other");

        var offMain = checkout.CheckWith("ledger-build", null, span);

        Assert.Equal(1, offMain.ExitCode);
        Assert.Contains("ledger-build: refused before any worker exists: the checkout is on 'other' and not on main, so no build was made from it", offMain.StandardError, StringComparison.Ordinal);
        Assert.Equal(string.Empty, offMain.StandardOutput.Trim());

        // Ahead of origin/main: refused the same way, naming the count.
        checkout.Run("checkout", "-q", "main");
        checkout.Commit("two");

        var ahead = checkout.CheckWith("ledger-build", null, span);

        Assert.Equal(1, ahead.ExitCode);
        Assert.Contains("the checkout holds 1 commit(s) origin/main does not have, so no build was made from it", ahead.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBuildWaitsForTheNightAndGoesOnFromTheSessionsNotYetWritten()
    {
        // A weekday at 22:55 UTC with a ten-minute chunk runs into the night's window and waits; at 22:40 it does not;
        // inside the window it waits; a Saturday never does; and a night holding its lock is waited for at any hour.
        Assert.True(SetupLedger.MustWait(new DateTimeOffset(2026, 10, 8, 22, 55, 0, TimeSpan.Zero), false, TimeSpan.FromMinutes(10)));
        Assert.False(SetupLedger.MustWait(new DateTimeOffset(2026, 10, 8, 22, 40, 0, TimeSpan.Zero), false, TimeSpan.FromMinutes(10)));
        Assert.True(SetupLedger.MustWait(new DateTimeOffset(2026, 10, 8, 23, 30, 0, TimeSpan.Zero), false, TimeSpan.FromMinutes(10)));
        Assert.False(SetupLedger.MustWait(new DateTimeOffset(2026, 10, 10, 23, 30, 0, TimeSpan.Zero), false, TimeSpan.FromMinutes(10)));
        Assert.True(SetupLedger.MustWait(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero), true, TimeSpan.FromMinutes(10)));

        // Five sessions, the second and the fourth written already: the span over all five writes the other three in
        // order; asked again it writes all five; a span inside the written ones writes nothing.
        DateOnly[] calendar = [.. Enumerable.Range(0, 5).Select(at => new DateOnly(2026, 10, 5).AddDays(at))];
        var stored = new HashSet<DateOnly> { calendar[1], calendar[3] };

        Assert.Equal([0, 2, 4], SetupLedger.SessionsToWrite(calendar, 0, 4, stored, again: false));
        Assert.Equal([0, 1, 2, 3, 4], SetupLedger.SessionsToWrite(calendar, 0, 4, stored, again: true));
        Assert.Empty(SetupLedger.SessionsToWrite(calendar, 1, 1, stored, again: false));
        Assert.Equal([2], SetupLedger.SessionsToWrite(calendar, 1, 3, stored, again: false));
    }
}
