namespace EquityBrief.Tests.Tools;

// tools/nights-keep, which the five scripts building the main checkout's commit call once their copy is built or reused
// and touched: three folders under the data root's folder of nights kept, this run's copy, the one night.build names and
// the most recently touched besides those, every other copy and a stale partial removed, a copy that cannot be removed
// named on stderr with the run carried on, and nothing outside the folder touched. The operator's ruling of 2026-10-25,
// item 4.
// see: Each night is built from a clean copy of the main checkout's own commit and never from its working tree, and refuses only a checkout off main or ahead of the remote's main
public class NightsKeepTests
{
    static readonly string[] Scripts = ["nightly", "localrun", "monthly", "offpeak", "ledger-build"];

    const string Call = "keep_nights \"$copies\" \"$copy\" \"$data/night.build\"";

    // The helper sourced and called as a script calls it, over the folders named.
    static ShellResult Keep(string bash, string copies, string copy, string build) =>
        Shell.Run(bash, ["-c", ". \"$1\" && keep_nights \"$2\" \"$3\" \"$4\"", "keep", Forward(Repository.Tool("nights-keep")), Forward(copies), Forward(copy), Forward(build)]);

    static string Forward(string path) => path.Replace('\\', '/');

    static string[] Folders(string folder) =>
        [.. Directory.GetDirectories(folder).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];

    [Fact]
    public void ARunKeepsItsCopyTheNightsBuildAndTheNewestBesideThemAndRemovesEveryOtherCopyAndAStalePartial()
    {
        var bash = Shell.Bash();

        if (bash is null)
        {
            Assert.False(OperatingSystem.IsWindows(), "Windows machines here carry a bash.");
            return;
        }

        using var root = new TemporaryDirectory();
        var data = Path.Combine(root.Path, "data");
        var nights = Path.Combine(data, "nights");
        var beside = Path.Combine(data, "beside");
        var build = Path.Combine(data, "night.build");
        var start = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

        // Six copies touched an hour apart, oldest first, a partial an earlier run left touched after them all, and a
        // folder beside the folder of nights.
        string[] commits = ["aaaaaaaaaaaa", "bbbbbbbbbbbb", "cccccccccccc", "dddddddddddd", "eeeeeeeeeeee", "ffffffffffff"];

        for (var at = 0; at < commits.Length; at++)
        {
            var folder = Path.Combine(nights, commits[at]);

            Directory.CreateDirectory(Path.Combine(folder, "src"));
            File.WriteAllText(Path.Combine(folder, "src", "built.txt"), commits[at]);
            Directory.SetLastWriteTimeUtc(folder, start.AddHours(at));
        }

        var partial = Path.Combine(nights, "gggggggggggg.partial");

        Directory.CreateDirectory(partial);
        File.WriteAllText(Path.Combine(partial, "half.txt"), "left by a run that stopped");
        Directory.SetLastWriteTimeUtc(partial, start.AddHours(10));
        Directory.CreateDirectory(beside);
        File.WriteAllText(Path.Combine(beside, "kept.txt"), "outside the folder of nights");

        // night.build names b's whole commit, as the night writes it.
        File.WriteAllText(build, "bbbbbbbbbbbb" + new string('0', 28) + "\n");

        // This run's copy is c: kept are c, b, which night.build names, and f, the newest touched besides them; a, d, e
        // and the partial are removed, and the folder beside and night.build are left alone.
        var kept = Keep(bash, nights, Path.Combine(nights, "cccccccccccc"), build);

        Assert.True(kept.ExitCode == 0, kept.Output);
        Assert.Equal(["bbbbbbbbbbbb", "cccccccccccc", "ffffffffffff"], Folders(nights));
        Assert.Equal("", kept.StandardError);
        Assert.Equal(["beside", "nights"], Folders(data));
        Assert.Equal("outside the folder of nights", File.ReadAllText(Path.Combine(beside, "kept.txt")));
        Assert.StartsWith("bbbbbbbbbbbb", File.ReadAllText(build), StringComparison.Ordinal);

        // Run again with nothing named: c, its copy, and f, the newest besides it, kept, b removed.
        File.Delete(build);

        var unnamed = Keep(bash, nights, Path.Combine(nights, "cccccccccccc"), build);

        Assert.True(unnamed.ExitCode == 0, unnamed.Output);
        Assert.Equal(["cccccccccccc", "ffffffffffff"], Folders(nights));

        // A folder that is not the data root's folder of nights is refused whole, named on stderr, and nothing in it
        // removed.
        var other = Keep(bash, data, Path.Combine(data, "nights"), build);

        Assert.True(other.ExitCode == 0, other.Output);
        Assert.Equal(["beside", "nights"], Folders(data));
        Assert.Contains("is not the data root's folder of nights, so no copy was removed", other.StandardError, StringComparison.Ordinal);

        // On Windows a copy whose file another process holds open cannot be removed: it is named in one line on
        // stderr and the run carries on, exiting nought.
        if (OperatingSystem.IsWindows())
        {
            var held = Path.Combine(nights, "hhhhhhhhhhhh");

            Directory.CreateDirectory(held);
            File.WriteAllText(Path.Combine(held, "open.txt"), "held");
            Directory.SetLastWriteTimeUtc(held, start);

            using (new FileStream(Path.Combine(held, "open.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var blocked = Keep(bash, nights, Path.Combine(nights, "cccccccccccc"), build);

                Assert.True(blocked.ExitCode == 0, blocked.Output);
                Assert.Equal($"nights: could not remove {Forward(held)}", Assert.Single(blocked.StandardError.Split('\n', StringSplitOptions.RemoveEmptyEntries)).TrimEnd('\r'));
            }
        }
    }

    [Fact]
    public void EachOfTheFiveScriptsKeepsTheFolderOfNightsOnceItsCopyIsTouchedAndBeforeTheWorkerStarts()
    {
        foreach (var script in Scripts)
        {
            var text = File.ReadAllText(Repository.Tool(script));
            var touched = text.IndexOf("touch \"$copy\"", StringComparison.Ordinal);
            var sourced = text.IndexOf(". \"$root/tools/nights-keep\"", StringComparison.Ordinal);
            var called = text.IndexOf(Call, StringComparison.Ordinal);
            var worker = text.IndexOf("dotnet \"$built\"", StringComparison.Ordinal);

            Assert.True(touched > 0 && sourced > touched && called > sourced && worker > called, $"{script} does not keep the folder of nights between touching its copy and starting the worker.");
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, System.Text.RegularExpressions.Regex.Escape(Call)));
        }

        // The night no longer removes a copy by its age.
        Assert.DoesNotContain("-mtime", File.ReadAllText(Repository.Tool("nightly")), StringComparison.Ordinal);
    }
}
