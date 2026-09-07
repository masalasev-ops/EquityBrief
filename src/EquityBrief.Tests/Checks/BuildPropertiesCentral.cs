namespace EquityBrief.Tests.Checks;

// build-properties-central. No project file states a target framework or a
// warning setting of its own, so both come from src/Directory.Build.props. A
// property set in six places is one a seventh project silently does without.
public class BuildPropertiesCentral
{
    static readonly string[] Centralised =
    [
        "TargetFramework",
        "TargetFrameworks",
        "Nullable",
        "TreatWarningsAsErrors",
        "WarningsAsErrors",
        "WarningsNotAsErrors",
        "NoWarn",
        "WarningLevel",
    ];

    const string DeclaresOne =
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
        "  <PropertyGroup>\n" +
        "    <TargetFramework>net10.0</TargetFramework>\n" +
        "    <NoWarn>CS1591</NoWarn>\n" +
        "  </PropertyGroup>\n" +
        "</Project>\n";

    [Fact]
    public void NoProjectFileDeclaresThemForItself()
    {
        var projects = Repository.ProjectFiles();

        // Six, because the layout block in CLAUDE.md lists six projects. A
        // seventh is a corpus change before it is a code change.
        Assert.Equal(6, projects.Count);

        foreach (var project in projects)
        {
            var declared = ProjectFile.Declares(File.ReadAllText(project), Centralised);

            Assert.True(
                declared.Count == 0,
                $"{Path.GetFileName(project)} declares {string.Join(", ", declared)}, which " +
                "src/Directory.Build.props already sets.");
        }
    }

    [Fact]
    public void TheDirectoryPropsDeclaresThem()
    {
        // Without this, the check above passes just as well over six projects
        // where nothing sets the properties anywhere.
        var declared = ProjectFile.Declares(File.ReadAllText(Repository.DirectoryBuildProps), Centralised);

        Assert.Contains("TargetFramework", declared);
        Assert.Contains("TreatWarningsAsErrors", declared);
        Assert.Contains("Nullable", declared);
    }

    [Fact]
    public void TheCheckReportsAPropertyWhereOneExists()
    {
        // The permanent proof that the assertion can fail.
        Assert.Equal(["NoWarn", "TargetFramework"], ProjectFile.Declares(DeclaresOne, Centralised));
    }
}
