using EquityBrief.Core.Time;
using EquityBrief.Tests.Checks;

namespace EquityBrief.Tests.Time;

// InvariantGlobalization stays false, because it is the setting that silently
// breaks IANA timezone lookup. Asserted twice: once over the running process,
// where the fault would actually appear, and once over the project files, where
// it would be introduced.
public class GlobalizationTests
{
    [Fact]
    public void TheRunningProcessCarriesTheTimezoneDatabase()
    {
        var invariant = AppContext.TryGetSwitch("System.Globalization.Invariant", out var value) && value;

        Assert.False(invariant);
        Assert.NotNull(SessionZones.Resolve(SessionZones.UnitedStates));
    }

    [Fact]
    public void NoProjectFileTurnsItOn()
    {
        var files = Repository.ProjectFiles().Append(Repository.DirectoryBuildProps).ToArray();

        Assert.True(files.Length >= 7, $"Read {files.Length} build files, expected at least 7.");

        var turnedOn = files
            .Where(file => string.Equals(
                ProjectFile.ValueOf(File.ReadAllText(file), "InvariantGlobalization"),
                "true",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(turnedOn);
    }

    [Fact]
    public void TheCheckReportsItWhereItIsTurnedOn()
    {
        // The permanent proof that the assertion can fail.
        Assert.Equal("true", ProjectFile.ValueOf(
            "<Project><PropertyGroup><InvariantGlobalization>true</InvariantGlobalization>" +
            "</PropertyGroup></Project>",
            "InvariantGlobalization"));

        Assert.Null(ProjectFile.ValueOf("<Project />", "InvariantGlobalization"));
    }
}
