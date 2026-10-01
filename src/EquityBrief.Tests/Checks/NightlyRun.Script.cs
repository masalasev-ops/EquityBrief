using EquityBrief.Core.Configuration;

namespace EquityBrief.Tests.Checks;

// tools/nightly builds each night from a clean copy of the main checkout's own commit and never from its
// working tree, refuses only a checkout off main or holding a commit origin/main does not have, names the
// store and the secrets file by absolute path from the main checkout, and runs the rest of a night from the
// commit that night was built from. Read in the script's own check mode over temporary git repositories,
// which prints what a run would build and run and runs nothing, so the suite reads the decision without
// building a worker in a scratch folder.
// see: Each night is built from a clean copy of the main checkout's own commit and never from its working tree, and refuses only a checkout off main or ahead of the remote's main
public partial class NightlyRun
{
    // A temporary repository on main with one commit under src/ and origin/main at that commit, holding a
    // copy of the script in a tools folder of its own, so the script's root is the repository.
    sealed class Checkout : IDisposable
    {
        public Checkout(string git, string bash)
        {
            Git = git;
            Bash = bash;
            Root = Directory.CreateTempSubdirectory("nightly-script-").FullName;

            Directory.CreateDirectory(Path.Combine(Root, "tools"));
            Directory.CreateDirectory(Path.Combine(Root, "src"));
            File.Copy(Repository.Tool("nightly"), Path.Combine(Root, "tools", "nightly"));

            Run("init", "-q", "-b", "main");
            Run("config", "user.email", "suite@example.invalid");
            Run("config", "user.name", "the suite");
            Run("config", "core.autocrlf", "false");
            First = Commit("one");
            Run("update-ref", "refs/remotes/origin/main", "HEAD");
        }

        public string Root { get; }

        public string Git { get; }

        public string Bash { get; }

        public string First { get; }

        public string Data => Path.Combine(Root, "data");

        public string Run(params string[] arguments)
        {
            var result = Shell.Run(Git, arguments, Root);

            Assert.True(result.ExitCode == 0, $"git {string.Join(' ', arguments)}: {result.Output}");

            return result.StandardOutput.Trim();
        }

        public string Commit(string name)
        {
            File.WriteAllText(Path.Combine(Root, "src", name + ".txt"), name + "\n");
            Run("add", "-A");
            Run("commit", "-q", "-m", name);

            return Run("rev-parse", "HEAD");
        }

        // The script's check mode: what a run would build and run, and nothing run.
        public ShellResult Check(IReadOnlyDictionary<string, string>? environment = null, params string[] arguments) =>
            Shell.Run(Bash, [Path.Combine(Root, "tools", "nightly"), .. arguments, "--check"], Root, environment);

        public void Dispose()
        {
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(Root, recursive: true);
        }
    }

    static (string Git, string Bash)? Tools()
    {
        var git = Shell.Locate("git");
        var bash = Shell.Bash();

        if (git is null || bash is null)
        {
            Assert.False(OperatingSystem.IsWindows(), "Windows machines here carry git and a bash.");

            return null;
        }

        return (git, bash);
    }

    static string Short(string commit) => commit[..12];

    // The script prints paths with forward slashes in the form the platform's tools read, so a path the
    // test composed is read the same way, and without regard to case where the file system has none.
    static void AssertNames(string path, string output) =>
        Assert.Contains(path.Replace('\\', '/'), output, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    [Fact]
    public void TheScriptRunsACleanOrDirtyCheckoutOnMainAndRefusesOneOffMainOrHoldingACommitOriginMainLacks()
    {
        if (Tools() is not var (git, bash))
        {
            return;
        }

        using var checkout = new Checkout(git, bash);

        // Clean, on main, at origin/main: the night would run the checkout's own commit from a copy of it
        // under the data root, with the store and the secrets named from the checkout by absolute path and
        // nothing copied.
        var clean = checkout.Check();

        Assert.True(clean.ExitCode == 0, clean.Output);
        Assert.Contains($"nightly: would run {checkout.First}, built from {Short(checkout.First)}", clean.StandardOutput, StringComparison.Ordinal);
        AssertNames(Path.Combine(checkout.Data, NightBuild.CopiesFolder, Short(checkout.First)) + ", to build", clean.StandardOutput);
        AssertNames("nightly: data root " + checkout.Data, clean.StandardOutput);
        AssertNames("nightly: secrets read from " + Path.Combine(checkout.Root, "src", "EquityBrief.Worker", "appsettings.Secrets.json") + ", none copied", clean.StandardOutput);
        Assert.False(File.Exists(Path.Combine(checkout.Data, NightBuild.RefusalFileName)));

        // A data root the environment names is the one the night is told, by absolute path.
        var elsewhere = Path.Combine(checkout.Root, "elsewhere");
        var named = checkout.Check(new Dictionary<string, string> { ["EquityBrief__DataRoot"] = elsewhere.Replace('\\', '/') });

        Assert.True(named.ExitCode == 0, named.Output);
        AssertNames("nightly: data root " + elsewhere, named.StandardOutput);
        AssertNames(Path.Combine(elsewhere, NightBuild.CopiesFolder, Short(checkout.First)), named.StandardOutput);

        // An edit under src/ and an untracked file cost no night: neither is built.
        File.WriteAllText(Path.Combine(checkout.Root, "src", "one.txt"), "edited and not committed\n");
        File.WriteAllText(Path.Combine(checkout.Root, "src", "stray.txt"), "never added\n");

        var dirty = checkout.Check();

        Assert.True(dirty.ExitCode == 0, dirty.Output);
        Assert.Contains($"nightly: would run {checkout.First}, built from {Short(checkout.First)}", dirty.StandardOutput, StringComparison.Ordinal);

        // Off main: refused before any worker exists, on stderr and in the file the pages read, which
        // carries the instant and the reason; back on main the refusal is cleared.
        checkout.Run("checkout", "-q", "-b", "other");

        var offMain = checkout.Check();
        var reason = "the checkout is on 'other' and not on main, so no night was built from it";

        Assert.Equal(1, offMain.ExitCode);
        Assert.Contains("nightly: refused before the first step: " + reason, offMain.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("would run", offMain.StandardOutput, StringComparison.Ordinal);

        var refusal = NightBuild.Refusal(checkout.Data);

        Assert.NotNull(refusal);
        Assert.Equal(reason, refusal.Reason);
        Assert.True(refusal.At > DateTimeOffset.UnixEpoch);

        checkout.Run("checkout", "-q", "main");

        Assert.Equal(0, checkout.Check().ExitCode);
        Assert.False(File.Exists(Path.Combine(checkout.Data, NightBuild.RefusalFileName)));

        // A commit origin/main does not have: refused, naming how many, until origin/main holds it.
        var second = checkout.Commit("two");
        var ahead = checkout.Check();

        Assert.Equal(1, ahead.ExitCode);
        Assert.Contains("the checkout holds 1 commit(s) origin/main does not have, so no night was built from it", ahead.StandardError, StringComparison.Ordinal);

        checkout.Run("update-ref", "refs/remotes/origin/main", "HEAD");

        var caughtUp = checkout.Check();

        Assert.True(caughtUp.ExitCode == 0, caughtUp.Output);
        Assert.Contains($"nightly: would run {second}, built from {Short(second)}", caughtUp.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("behind origin/main", caughtUp.StandardOutput, StringComparison.Ordinal);

        // Behind origin/main: the night runs the checkout's own commit and says how far behind it is.
        checkout.Commit("three");
        checkout.Run("update-ref", "refs/remotes/origin/main", "HEAD");
        checkout.Run("reset", "-q", "--hard", "HEAD~1");

        var behind = checkout.Check();

        Assert.True(behind.ExitCode == 0, behind.Output);
        Assert.Contains($"nightly: would run {second}, built from {Short(second)}, 1 commit(s) behind origin/main", behind.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRestOfANightRunsTheCommitThatNightWasBuiltFromAndABuildAlreadyMadeIsReused()
    {
        if (Tools() is not var (git, bash))
        {
            return;
        }

        using var checkout = new Checkout(git, bash);

        // The newest night was built from the first commit, and a newer one has landed since.
        Directory.CreateDirectory(checkout.Data);
        File.WriteAllText(Path.Combine(checkout.Data, NightBuild.CommitFileName), checkout.First + "\n");

        var second = checkout.Commit("two");

        checkout.Run("update-ref", "refs/remotes/origin/main", "HEAD");

        // A night runs the checkout's commit; the rest of the night runs the night's own.
        var night = checkout.Check();

        Assert.True(night.ExitCode == 0, night.Output);
        Assert.Contains($"nightly: would run {second}, built from {Short(second)}", night.StandardOutput, StringComparison.Ordinal);

        var rest = checkout.Check(null, "--resume");

        Assert.True(rest.ExitCode == 0, rest.Output);
        Assert.Contains($"nightly: would run {checkout.First}, built from {Short(checkout.First)}, the newest night's own commit rather than the checkout's", rest.StandardOutput, StringComparison.Ordinal);
        AssertNames(Path.Combine(checkout.Data, NightBuild.CopiesFolder, Short(checkout.First)), rest.StandardOutput);

        // A copy already holding the worker is run as it is.
        var build = Path.Combine(checkout.Data, NightBuild.CopiesFolder, Short(second), "src", "EquityBrief.Worker", "bin", "Release", Repository.Framework);

        Directory.CreateDirectory(build);
        File.WriteAllText(Path.Combine(build, NightBuild.WorkerAssembly), "a worker built already");

        var reused = checkout.Check();

        Assert.True(reused.ExitCode == 0, reused.Output);
        AssertNames(Path.Combine(checkout.Data, NightBuild.CopiesFolder, Short(second)) + ", built already", reused.StandardOutput);
        Assert.Equal(build, NightBuild.WorkerBuild(checkout.Data, second));

        // The rest of a night whose commit the repository no longer holds is refused by name.
        File.WriteAllText(Path.Combine(checkout.Data, NightBuild.CommitFileName), "0123456789abcdef0123456789abcdef01234567\n");

        var gone = checkout.Check(null, "--resume");

        Assert.Equal(1, gone.ExitCode);
        Assert.Contains("the newest night was built from 0123456789abcdef0123456789abcdef01234567, which this repository no longer holds", gone.StandardError, StringComparison.Ordinal);
    }
}
