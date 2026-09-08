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
    const string Framework = @"net[0-9]+\.[0-9]+";
    const string Band = @"[0-9]+\.[0-9]+\.[0-9]xx";

    // The specs, which is where a version may be stated. Records are exempt for
    // the reason given above. Read once so both checks scan the same population
    // and neither can quietly narrow to the file that happens to state it.
    static IReadOnlyDictionary<string, string> Specs() =>
        Corpus.Specs.ToDictionary(spec => spec, Corpus.Read, StringComparer.Ordinal);

    [Fact]
    public void TheFrameworkTheSpecsStateIsTheOneTheBuildUses()
    {
        var built = Versions.FrameworkIn(File.ReadAllText(Repository.DirectoryBuildProps));
        var specs = Specs();
        var stated = Versions.MentionsIn(specs, Framework);

        // Two scopes, and only the second carries the property. The documents
        // opened is a fact about the corpus, so it is context and carries no
        // floor: a floor on it is satisfied by opening a document, and a run
        // finding no mention in any of them would pass having compared nothing.
        //
        // The comparisons are what this check is about. That number cannot be
        // moved by adding a document or by adding a sentence, only by a mention
        // that agrees with the build file, which is the thing being pinned.
        Assert.True(
            stated.Count >= 2,
            $"Compared {stated.Count} framework mentions against {Repository.DirectoryBuildProps}, " +
            $"expected at least 2, over {specs.Count} specs read.");

        Assert.Empty(Versions.Disagreeing(stated, built, "framework mention"));
    }

    [Fact]
    public void TheFeatureBandTheSpecsStateIsTheOneGlobalJsonPins()
    {
        var pinned = Versions.FeatureBand(Versions.SdkVersionIn(File.ReadAllText(Repository.SdkPin)));
        var specs = Specs();
        var stated = Versions.MentionsIn(specs, Band);

        Assert.True(
            stated.Count >= 2,
            $"Compared {stated.Count} band mentions against {Repository.SdkPin}, " +
            $"expected at least 2, over {specs.Count} specs read.");

        Assert.Empty(Versions.Disagreeing(stated, pinned, "band mention"));
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

    [Fact]
    public void ADocumentStatingAVersionTheBuildDoesNotIsReported()
    {
        // The permanent proof over a constructed corpus, naming the document
        // rather than only the value, which is what a person needs to open.
        var documents = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["agrees.md"] = "the projects target `net10.0`",
            ["disagrees.md"] = "the projects target `net9.0`",
        };

        var disagreeing = Versions.Disagreeing(
            Versions.MentionsIn(documents, Framework), "net10.0", "framework mention");

        Assert.Equal("disagrees.md", Assert.Single(disagreeing).Document);
    }

    [Fact]
    public void AScanThatComparedNothingFailsRatherThanPassingOverAnEmptyResult()
    {
        // The other half, and the one a floor on documents opened would miss.
        // Both documents are read and neither states a framework, so there is
        // nothing to disagree and "none of them disagreed" is vacuously true.
        // Assert.Empty over an empty list passes, so the refusal has to happen
        // before the assertion is reached rather than inside it.
        var documents = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["silent.md"] = "this document states no framework at all",
            ["also-silent.md"] = "and neither does this one",
        };

        var mentions = Versions.MentionsIn(documents, Framework);

        Assert.Empty(mentions);

        var refusal = Assert.Throws<InvalidOperationException>(
            () => Versions.Disagreeing(mentions, "net10.0", "framework mention"));

        Assert.Contains("must fail rather than pass over an empty scan", refusal.Message, StringComparison.Ordinal);
    }
}
