using System.Text.Json;

namespace EquityBrief.Tests.Checks;

// Reads the libraries a compiled dependency file says an assembly ships. Kept
// apart from the assertions so the same reader can be pointed at a manifest that
// must pass and at one that must fail.
internal static class DependencyManifest
{
    internal static IReadOnlyList<string> Libraries(string dependencyFile)
    {
        using var document = JsonDocument.Parse(dependencyFile);

        if (!document.RootElement.TryGetProperty("libraries", out var libraries))
        {
            throw new InvalidOperationException(
                "The dependency file carries no libraries section. Reporting nothing from a " +
                "file that cannot be read is the under-reporting this check exists to avoid.");
        }

        return libraries.EnumerateObject()
            .Select(library => library.Name.Split('/')[0])
            .ToArray();
    }
}
