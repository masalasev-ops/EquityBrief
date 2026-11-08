using EquityBrief.Core.Configuration;

namespace EquityBrief.Tests.Checks;

// On 2026-11-07 a branch's Debug build was served over the live store for a read-back while the monthly run wrote it. A
// branch build is served over a copy of the store made for the read-back, and the read surface refuses to start a branch
// build's server on the live store's path: a Debug build, or a build in a checkout off main, whose store is the one under
// the checkout's data folder.
// see: A branch build is served over a copy of the store, and its server refuses to start on the live store
public class BranchBuildGuard
{
    static string Checkout(TemporaryDirectory folder, string? head)
    {
        if (head is not null)
        {
            Directory.CreateDirectory(Path.Combine(folder.Path, ".git"));
            File.WriteAllText(Path.Combine(folder.Path, ".git", "HEAD"), head + "\n");
        }

        return folder.Path;
    }

    [Fact]
    public void ABranchBuildIsRefusedOnTheLiveStoreAndServedOverACopy()
    {
        using var branch = new TemporaryDirectory();
        using var main = new TemporaryDirectory();
        using var detached = new TemporaryDirectory();
        using var copy = new TemporaryDirectory();

        var onBranch = Checkout(branch, "ref: refs/heads/plan-diagrams-back");
        var onMain = Checkout(main, "ref: refs/heads/main");
        var onCommit = Checkout(detached, "7f057a5642e1d8bba7c3b84bd70b98e1cbd0d4e1");
        var clean = Checkout(copy, null);

        Assert.Equal("plan-diagrams-back", BranchBuild.BranchOf(onBranch));
        Assert.Equal("main", BranchBuild.BranchOf(onMain));
        Assert.Equal("7f057a5642e1d8bba7c3b84bd70b98e1cbd0d4e1", BranchBuild.BranchOf(onCommit));
        Assert.Null(BranchBuild.BranchOf(clean));
        Assert.Null(BranchBuild.BranchOf(null));

        // The live store as the surface resolves its default data root against the checkout.
        string Live(string checkout) => StoreLocation.Within(checkout, BranchBuild.LiveFolder).DatabaseFile;

        // A branch's build, Debug or Release, and a Debug build of main, are refused on the live store.
        var refused = BranchBuild.Refusal(onBranch, Live(onBranch), "Release", BranchBuild.BranchOf(onBranch));

        Assert.Equal(
            $"Refused to start: a build on the branch plan-diagrams-back would serve the live store at {Path.Combine(onBranch, "data", "equitybrief.db")}. A branch build is served over a copy of the store made for the read-back: copy the store into a folder of its own and set EquityBrief__DataRoot to that folder. The live store is served by main's Release build alone.",
            refused);
        Assert.StartsWith("Refused to start: a Debug build on the branch plan-diagrams-back ", BranchBuild.Refusal(onBranch, Live(onBranch), "Debug", "plan-diagrams-back"), StringComparison.Ordinal);
        Assert.StartsWith("Refused to start: a Debug build would serve ", BranchBuild.Refusal(onMain, Live(onMain), "Debug", "main"), StringComparison.Ordinal);
        Assert.NotNull(BranchBuild.Refusal(onCommit, Live(onCommit), "Release", BranchBuild.BranchOf(onCommit)));

        // Main's Release build serves the live store, and a folder holding no repository, as the night's clean copy of main's
        // commit or a checkout the suite constructs, holds no live store to refuse.
        Assert.Null(BranchBuild.Refusal(onMain, Live(onMain), "Release", "main"));
        Assert.Null(BranchBuild.Refusal(clean, Live(clean), "Release", null));
        Assert.Null(BranchBuild.Refusal(clean, Live(clean), "Debug", BranchBuild.BranchOf(clean)));

        // Any build is served over a copy: a store anywhere but the checkout's data folder.
        var readBack = Path.Combine(copy.Path, "readback", "equitybrief.db");

        Assert.Null(BranchBuild.Refusal(onBranch, readBack, "Debug", "plan-diagrams-back"));
        Assert.Null(BranchBuild.Refusal(onBranch, Path.Combine(onBranch, "data-ci", "equitybrief.db"), "Debug", "plan-diagrams-back"));
        Assert.Null(BranchBuild.Refusal(null, Live(onBranch), "Debug", null));
    }

    // The surface asks before it writes its start row or listens: the refusal stands after the host is built and before the
    // start row, and a refused start writes the reason, sets its own exit code and returns.
    [Fact]
    public void TheReadSurfaceAsksBeforeItWritesItsStartRowOrListens()
    {
        var program = File.ReadAllText(Path.Combine(Repository.Root, "src", "EquityBrief.Api", "Program.cs"));
        var asked = program.IndexOf("if (BranchBuild.Refusal(", StringComparison.Ordinal);
        var built = program.IndexOf("var app = builder.Build();", StringComparison.Ordinal);
        var started = program.IndexOf(".RecordStartAsync(", StringComparison.Ordinal);
        var listened = program.IndexOf("app.Run();", StringComparison.Ordinal);

        Assert.True(built >= 0 && asked > built && started > asked && listened > started, $"built {built}, asked {asked}, started {started}, listened {listened}");
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(program, @"BranchBuild\.Refusal\("));

        var refusing = program[asked..started];

        Assert.Contains("app.Services.GetRequiredService<StoreLocation>().DatabaseFile", refusing, StringComparison.Ordinal);
        Assert.Contains("AssemblyConfigurationAttribute", refusing, StringComparison.Ordinal);
        Assert.Contains("BranchBuild.BranchOf(checkout)", refusing, StringComparison.Ordinal);
        Assert.Contains("Environment.ExitCode = BranchBuild.Refused;", refusing, StringComparison.Ordinal);
        Assert.Contains("return;", refusing, StringComparison.Ordinal);
        Assert.NotEqual(0, BranchBuild.Refused);
        Assert.NotEqual(1, BranchBuild.Refused);
    }
}
