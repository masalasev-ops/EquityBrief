using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Merge;
using Xunit;

namespace EquityBrief.Tests.Checks;

// From the 24.47 correction of 2026-11-28: tools/merge asks the worker's writers verb before it merges, which names every worker
// process running by the night copy it runs from, or its own folder, and refuses where any runs.
// see: tools/merge refuses while any writer runs, read off the machine's processes and the run log, and names the writer
public class MergeGuardTests
{
    [Fact]
    public void AWorkerUnderANightCopyIsNamedByTheCopyAndOneElsewhereByItsFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "equitybrief-merge-guard", "data");
        var nights = Path.Combine(root, EquityBrief.Core.Configuration.NightBuild.CopiesFolder);
        var fromCopy = Path.Combine(nights, "57488326ab12", "src", "EquityBrief.Worker", "bin", "Release", "net10.0", "EquityBrief.Worker.exe");
        var elsewhere = Path.Combine(Path.GetTempPath(), "equitybrief-merge-guard", "checkout", "EquityBrief.Worker.exe");

        Assert.Equal("57488326ab12", MergeGuard.CopyOf(fromCopy, nights));
        Assert.Null(MergeGuard.CopyOf(elsewhere, nights));
        Assert.Null(MergeGuard.CopyOf(null, nights));

        // A folder whose name only begins like the nights folder is not under it.
        Assert.Null(MergeGuard.CopyOf(Path.Combine(root, "nights-old", "x", "EquityBrief.Worker.exe"), nights));

        Assert.Equal("a worker, process 41, running from the night copy 57488326ab12", MergeGuard.WriterWords(41, fromCopy, nights));
        Assert.Equal($"a worker, process 42, running from {Path.GetDirectoryName(elsewhere)}", MergeGuard.WriterWords(42, elsewhere, nights));
        Assert.Equal("a worker, process 43, whose file could not be read", MergeGuard.WriterWords(43, null, nights));
    }

    [Fact]
    public void TheMergeScriptRefusesWhereTheWritersVerbNamesAWriter()
    {
        var merge = File.ReadAllText(Path.Combine(Repository.Root, "tools", "merge"));
        var asked = merge.IndexOf("if ! dotnet run --project src/EquityBrief.Worker -- writers; then", StringComparison.Ordinal);
        var refused = merge.IndexOf("echo \"merge: refused while a writer runs, named above; merge once it has finished\" >&2", StringComparison.Ordinal);

        Assert.True(asked > 0 && refused > asked, "The merge script does not refuse on the writers verb.");
        Assert.True(refused < merge.IndexOf("gh pr merge", StringComparison.Ordinal), "The merge script merges before it asks.");
        Assert.Equal(3, MergeGuard.Refused);
    }
}
