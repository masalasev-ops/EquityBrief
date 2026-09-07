namespace EquityBrief.Tests;

// Where the checks read the repository from. The suite asserts properties of the
// checkout, so it has to find the checkout, and it has to fail loudly rather
// than quietly assert nothing when it cannot.
internal static class Repository
{
    internal static string Root { get; } = FindRoot();

    // Taken from the test assembly's own output path rather than written down,
    // so neither the configuration nor the framework version is stated twice.
    internal static string Framework { get; } = new DirectoryInfo(AppContext.BaseDirectory).Name;

    internal static string Configuration { get; } =
        new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;

    internal static string DirectoryBuildProps => Path.Combine(Root, "src", "Directory.Build.props");

    internal static string Rules => Path.Combine(Root, "CLAUDE.md");

    internal static string SdkPin => Path.Combine(Root, "global.json");

    internal static string BuildPlan => Path.Combine(Root, "docs", "BUILD_PLAN.md");

    internal static string Workflow => Path.Combine(Root, ".github", "workflows", "ci.yml");

    internal static IReadOnlyList<string> ProjectFiles() =>
        Directory.GetFiles(Path.Combine(Root, "src"), "*.csproj", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    internal static string BuildOutput(string project, string file) =>
        Path.Combine(Root, "src", project, "bin", Configuration, Framework, file);

    static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EquityBrief.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            $"No EquityBrief.slnx above {AppContext.BaseDirectory}. The checks read the " +
            "checkout, so not finding it is a failure of the check and never a pass.");
    }
}
