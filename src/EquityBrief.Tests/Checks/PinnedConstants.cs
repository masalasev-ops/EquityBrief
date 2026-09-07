namespace EquityBrief.Tests.Checks;

// pinned-constants, over the constants 0.1 introduces: the framework version and
// the SDK feature band. Each is stated in a spec and set in a build file, and
// nothing else would notice the two drifting apart.
//
// Records are exempt. CHANGELOG.md holds prior text that must never be edited to
// match a later value, and PROGRESS.md holds dated measurements, so neither is
// scanned. This is the same exemption stated-counts already makes.
public class PinnedConstants
{
    [Fact]
    public void TheFrameworkTheSpecsStateIsTheOneTheBuildUses()
    {
        var built = Versions.FrameworkIn(File.ReadAllText(Repository.DirectoryBuildProps));

        var stated = Versions.Occurrences(File.ReadAllText(Repository.Rules), @"net[0-9]+\.[0-9]+")
            .Concat(Versions.Occurrences(File.ReadAllText(Repository.BuildPlan), @"net[0-9]+\.[0-9]+"))
            .ToArray();

        Assert.True(stated.Length >= 2, $"Found {stated.Length} framework mentions, expected at least 2.");
        Assert.All(stated, mention => Assert.Equal(built, mention));
    }

    [Fact]
    public void TheFeatureBandTheRulesStateIsTheOneGlobalJsonPins()
    {
        var pinned = Versions.FeatureBand(Versions.SdkVersionIn(File.ReadAllText(Repository.SdkPin)));
        var stated = Versions.Occurrences(File.ReadAllText(Repository.Rules), @"[0-9]+\.[0-9]+\.[0-9]xx");

        Assert.True(stated.Count >= 2, $"Found {stated.Count} band mentions, expected at least 2.");
        Assert.All(stated, mention => Assert.Equal(pinned, mention));
    }

    [Fact]
    public void TheWorkflowInstallsAnSdkThePinWillAccept()
    {
        var sdk = Versions.SdkVersionIn(File.ReadAllText(Repository.SdkPin));
        var framework = Versions.FrameworkIn(File.ReadAllText(Repository.DirectoryBuildProps));
        var installs = Versions.Occurrences(File.ReadAllText(Repository.Workflow), "dotnet-version: '[^']+'");

        Assert.True(installs.Count >= 2, $"Found {installs.Count} install steps, expected at least 2.");
        Assert.All(installs, step => Assert.Equal(Versions.MajorMinor(sdk), Versions.MajorMinor(step)));
        Assert.Equal(Versions.MajorMinor(sdk), Versions.MajorMinor(framework));
    }

    [Fact]
    public void TheCheckReportsVersionsThatDisagree()
    {
        // The permanent proof that the assertions above can fail: every
        // extractor returns a different value for different input.
        Assert.Equal("net9.0", Versions.Occurrences("targeting `net9.0`", @"net[0-9]+\.[0-9]+").Single());
        Assert.Equal("9.0", Versions.MajorMinor("net9.0"));
        Assert.Equal("10.0.4xx", Versions.FeatureBand("10.0.400"));
    }

    [Fact]
    public void AVersionThatCannotBeReadFailsRatherThanDefaulting()
    {
        Assert.Throws<FormatException>(() => Versions.MajorMinor("no version here"));
        Assert.Throws<FormatException>(() => Versions.FeatureBand("10.0"));
    }
}
