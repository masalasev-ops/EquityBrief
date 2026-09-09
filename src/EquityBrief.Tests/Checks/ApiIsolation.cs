namespace EquityBrief.Tests.Checks;

// api-isolation. EquityBrief.Api carries no transitive reference to
// EquityBrief.Worker, read from the compiled dependency file rather than the
// project file: a project file says what was asked for and the dependency file
// says what shipped, and only the second is the property.
public class ApiIsolation
{
    const string Forbidden = "EquityBrief.Worker";

    [Fact]
    public void TheApiDoesNotShipTheWorker()
    {
        Assert.DoesNotContain(Forbidden, LibrariesOf("EquityBrief.Api"));
    }

    [Fact]
    public void TheApiShipsTheThreeItReferences()
    {
        // The scope carrying the property is the library list, and this is its
        // floor. The named projects are the assertion; the count is context, set
        // far enough below its value that a package added at 1.5 cannot move it.
        var libraries = LibrariesOf("EquityBrief.Api");

        Assert.Contains("EquityBrief.Core", libraries);
        Assert.Contains("EquityBrief.Data", libraries);
        Assert.Contains("EquityBrief.Web", libraries);
        Assert.True(libraries.Count >= 4, $"Read {libraries.Count} libraries, expected at least 4.");
    }

    [Fact]
    public void TheCheckReportsAForbiddenReferenceWhereOneExists()
    {
        // The permanent proof that the assertion can fail. EquityBrief.Tests
        // references every project by design and is the exemption CLAUDE.md
        // names, so its dependency file is a real example of what api-isolation
        // has to catch rather than a string written to be caught.
        Assert.Contains(Forbidden, LibrariesOf("EquityBrief.Tests"));
    }

    static IReadOnlyList<string> LibrariesOf(string project)
    {
        var path = Repository.BuildOutput(project, $"{project}.deps.json");

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"No dependency file at {path}. api-isolation reads what the build produced, " +
                "so a missing file fails the check and never passes it.", path);
        }

        return DependencyManifest.Libraries(File.ReadAllText(path));
    }
}
