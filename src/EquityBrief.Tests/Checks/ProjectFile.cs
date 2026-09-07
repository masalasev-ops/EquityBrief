using System.Xml.Linq;

namespace EquityBrief.Tests.Checks;

// Which of a named set of MSBuild properties a project file declares for itself.
internal static class ProjectFile
{
    internal static IReadOnlyList<string> Declares(string xml, IEnumerable<string> properties)
    {
        var wanted = properties.ToHashSet(StringComparer.Ordinal);

        return XDocument.Parse(xml).Descendants()
            .Select(element => element.Name.LocalName)
            .Where(wanted.Contains)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    internal static string? ValueOf(string xml, string property) =>
        XDocument.Parse(xml).Descendants()
            .Where(element => element.Name.LocalName == property)
            .Select(element => element.Value.Trim())
            .FirstOrDefault();
}
