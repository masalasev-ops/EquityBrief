namespace EquityBrief.Tests.Tools;

// tools/ci.sh writes the suite's result beside a stamp naming the commit it is the result of, only where
// the tracked tree stood clean at that commit before the build and still did after the suite, and
// tools/verify-phase reads that result in place of a run of its own only where the stamp names the commit
// its tree stands at and the tree is clean. Read by running the two scripts over a temporary repository
// with a dotnet of the test's own first on the path, which writes down what it was asked and runs nothing,
// so each decision is read without building anything.
// see: The phase report reads the checkpoint script's suite result over the same commit and a clean tree
public class SuiteReuseTests
{
    const string ReadingTheResult = "verify-phase: reading the suite result tools/ci wrote over ";
    const string RunningTheSuite = "verify-phase: running the suite";

    // A temporary repository on one commit holding copies of the three scripts a run of either reaches,
    // with the folders the scripts write ignored as the repository's own are, so a run leaves the tree clean.
    sealed class Checkout : IDisposable
    {
        readonly TemporaryDirectory directory = new();

        public Checkout(string git, string bash)
        {
            Git = git;
            Bash = bash;

            Directory.CreateDirectory(Path.Combine(Root, "tools"));
            Directory.CreateDirectory(Path.Combine(Root, "bin"));

            foreach (var script in new[] { "ci.sh", "ci.ps1", "verify-phase", "migrate", "migrate.ps1", "run-bash.ps1" })
            {
                File.Copy(Repository.Tool(script), Path.Combine(Root, "tools", script));
            }

            File.WriteAllText(Path.Combine(Root, ".gitignore"), "artifacts/\ndata-ci/\nbin/\ndotnet.log\n");

            // The dotnet the scripts find first, in the form bash runs and the form PowerShell runs on
            // Windows, each writing its arguments to the log and exiting as the test asks.
            var fake = Path.Combine(Root, "bin", "dotnet");

            File.WriteAllText(
                fake,
                "#!/usr/bin/env bash\n"
                + "printf '%s\\n' \"$*\" >> \"$SUITE_REUSE_LOG\"\n"
                + "if [ \"$1\" = test ]; then exit \"${SUITE_REUSE_TEST_EXIT:-0}\"; fi\n"
                + "exit 0\n");

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            File.WriteAllText(
                fake + ".cmd",
                "@echo off\r\n"
                + "echo %* >> \"%SUITE_REUSE_LOG%\"\r\n"
                + "if \"%1\"==\"test\" exit /b %SUITE_REUSE_TEST_EXIT%\r\n"
                + "exit /b 0\r\n");

            Run("init", "-q", "-b", "main");
            Run("config", "user.email", "suite@example.invalid");
            Run("config", "user.name", "the suite");
            Run("config", "core.autocrlf", "false");
            Run("add", "-A");
            Run("commit", "-q", "-m", "one");
            Commit = Run("rev-parse", "HEAD");
        }

        public string Root => directory.Path;

        public string Git { get; }

        public string Bash { get; }

        public string Commit { get; private set; }

        public string Log => Path.Combine(Root, "dotnet.log");

        public string Stamp => Path.Combine(Root, "artifacts", "suite.commit");

        public string Result => Path.Combine(Root, "artifacts", "suite.trx");

        public string Run(params string[] arguments)
        {
            var result = Shell.RunRetrying(Git, arguments, Root);

            Assert.True(result.ExitCode == 0, $"git {string.Join(' ', arguments)}: {result.Output}");

            return result.StandardOutput.Trim();
        }

        // One of the scripts, under bash or under the PowerShell host given, run with the test's dotnet
        // first on the path and its log emptied first, so each run's asks are read on their own.
        public ShellResult Script(string name, int testExit = 0, string? host = null)
        {
            File.WriteAllText(Log, "");

            var script = Path.Combine(Root, "tools", name);
            var environment = new Dictionary<string, string>
            {
                ["PATH"] = Path.Combine(Root, "bin") + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
                ["SUITE_REUSE_LOG"] = Log.Replace('\\', '/'),
                ["SUITE_REUSE_TEST_EXIT"] = testExit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };

            return host is null
                ? Shell.Run(Bash, [script], Root, environment)
                : Shell.Run(host, ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script], Root, environment);
        }

        // The verbs the test's dotnet was asked, in order.
        public string[] Asked() => File.ReadAllLines(Log).Select(line => line.Split(' ')[0]).ToArray();

        public string StampText() => File.Exists(Stamp) ? File.ReadAllText(Stamp) : "missing";

        public void Dirty() => File.WriteAllText(Path.Combine(Root, "stray.txt"), "an edit the commit does not hold\n");

        // The stray file committed, so the tree is clean again at a commit the stamp before it does not name.
        public void CommitTheStray()
        {
            Run("add", "-A");
            Run("commit", "-q", "-m", "two");
            Commit = Run("rev-parse", "HEAD");
        }

        public void Dispose()
        {
            // Git's objects are read-only, which the temporary directory's removal does not undo.
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            directory.Dispose();
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

    [Fact]
    public void TheCheckpointScriptStampsTheSuitesResultWithTheCommitOverACleanTreeAndEmptiesItOverADirtyOne()
    {
        if (Tools() is not var (git, bash))
        {
            return;
        }

        using var checkout = new Checkout(git, bash);

        // Clean at the commit before and after: the stamp is the commit, one line, and the suite was asked
        // for its result where the report reads it.
        var clean = checkout.Script("ci.sh");

        Assert.True(clean.ExitCode == 0, clean.Output);
        Assert.Equal(checkout.Commit + "\n", checkout.StampText());
        Assert.Contains("test EquityBrief.slnx --no-build --results-directory artifacts --logger trx;LogFileName=suite.trx", File.ReadAllText(checkout.Log), StringComparison.Ordinal);

        // A failing suite fails the script at its step and is stamped all the same, since the result is
        // the commit's whether it passed or not.
        var failing = checkout.Script("ci.sh", testExit: 1);

        Assert.Equal(1, failing.ExitCode);
        Assert.Contains("ci: failed at step: suite", failing.Output, StringComparison.Ordinal);
        Assert.Equal(checkout.Commit + "\n", checkout.StampText());

        // A stray file the commit does not hold: the suite runs over no commit's tree, and the stamp is
        // emptied, which names no commit.
        checkout.Dirty();

        var dirty = checkout.Script("ci.sh");

        Assert.True(dirty.ExitCode == 0, dirty.Output);
        Assert.Equal("", checkout.StampText());
    }

    [Fact]
    public void TheWindowsCheckpointScriptStampsTheSameWayWithNoCarriageReturn()
    {
        if (Tools() is not var (git, bash))
        {
            return;
        }

        var host = Shell.PowerShellHost();

        if (host is null)
        {
            Assert.False(OperatingSystem.IsWindows(), "Windows machines carry a PowerShell.");

            return;
        }

        using var checkout = new Checkout(git, bash);

        var clean = checkout.Script("ci.ps1", host: host);

        Assert.True(clean.ExitCode == 0, clean.Output);
        Assert.Equal(checkout.Commit + "\n", checkout.StampText());
        Assert.Contains("test EquityBrief.slnx --no-build --results-directory artifacts --logger trx;LogFileName=suite.trx", File.ReadAllText(checkout.Log), StringComparison.Ordinal);

        var failing = checkout.Script("ci.ps1", testExit: 1, host: host);

        Assert.Equal(1, failing.ExitCode);
        Assert.Contains("ci: failed at step: suite", failing.Output, StringComparison.Ordinal);
        Assert.Equal(checkout.Commit + "\n", checkout.StampText());

        checkout.Dirty();

        var dirty = checkout.Script("ci.ps1", host: host);

        Assert.True(dirty.ExitCode == 0, dirty.Output);
        Assert.Equal("", checkout.StampText());
    }

    [Fact]
    public void ThePhaseReportReadsTheStampedResultOverTheSameCommitAndACleanTreeAndRunsTheSuiteOtherwise()
    {
        if (Tools() is not var (git, bash))
        {
            return;
        }

        using var checkout = new Checkout(git, bash);

        // No result and no stamp: the suite runs here, then the report.
        var fresh = checkout.Script("verify-phase");

        Assert.True(fresh.ExitCode == 0, fresh.Output);
        Assert.Contains(RunningTheSuite, fresh.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(["test", "run"], checkout.Asked());

        // The checkpoint script's result over this commit, with its stamp: read, and the suite not run.
        Directory.CreateDirectory(Path.Combine(checkout.Root, "artifacts"));
        File.WriteAllText(checkout.Result, "<TestRun />\n");
        File.WriteAllText(checkout.Stamp, checkout.Commit + "\n");

        var stamped = checkout.Script("verify-phase");

        Assert.True(stamped.ExitCode == 0, stamped.Output);
        Assert.Contains(ReadingTheResult + checkout.Commit, stamped.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(["run"], checkout.Asked());
        Assert.True(File.Exists(checkout.Result));
        Assert.Equal(checkout.Commit + "\n", checkout.StampText());

        // The same stamp over a tree holding a stray file: the suite runs here, and the stamp goes with the
        // result it stood beside, so this run's result is never read as the checkpoint script's.
        checkout.Dirty();

        var dirty = checkout.Script("verify-phase");

        Assert.True(dirty.ExitCode == 0, dirty.Output);
        Assert.Contains(RunningTheSuite, dirty.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(["test", "run"], checkout.Asked());
        Assert.Equal("missing", checkout.StampText());

        // The stray file committed: a clean tree at a commit the stamp does not name runs the suite here,
        // the result beside the stamp being the earlier commit's.
        var earlier = checkout.Commit;

        checkout.CommitTheStray();
        File.WriteAllText(checkout.Result, "<TestRun />\n");
        File.WriteAllText(checkout.Stamp, earlier + "\n");

        var other = checkout.Script("verify-phase");

        Assert.NotEqual(earlier, checkout.Commit);
        Assert.True(other.ExitCode == 0, other.Output);
        Assert.Contains(RunningTheSuite, other.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(["test", "run"], checkout.Asked());
        Assert.False(File.Exists(checkout.Result));

        // The right stamp with no result beside it runs the suite here as well.
        File.WriteAllText(checkout.Stamp, checkout.Commit + "\n");

        var unwritten = checkout.Script("verify-phase");

        Assert.True(unwritten.ExitCode == 0, unwritten.Output);
        Assert.Contains(RunningTheSuite, unwritten.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(["test", "run"], checkout.Asked());

        // The stamp as the Windows script writes it, one line feed and no carriage return, and one a
        // carriage return crept into, read the same.
        File.WriteAllText(checkout.Result, "<TestRun />\n");
        File.WriteAllText(checkout.Stamp, checkout.Commit + "\r\n");

        var carriage = checkout.Script("verify-phase");

        Assert.True(carriage.ExitCode == 0, carriage.Output);
        Assert.Contains(ReadingTheResult + checkout.Commit, carriage.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(["run"], checkout.Asked());
    }
}
