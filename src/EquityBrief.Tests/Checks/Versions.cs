using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace EquityBrief.Tests.Checks;

// Pulls the framework and SDK versions out of the files that state them. Every
// extractor throws on input it cannot read rather than returning a default,
// because a default that happens to match is a pass nobody earned.
internal static class Versions
{
    internal static string FrameworkIn(string propsXml) =>
        XDocument.Parse(propsXml).Descendants()
            .Where(element => element.Name.LocalName == "TargetFramework")
            .Select(element => element.Value.Trim())
            .Single();

    internal static string SdkVersionIn(string globalJson)
    {
        using var document = JsonDocument.Parse(globalJson);
        return document.RootElement.GetProperty("sdk").GetProperty("version").GetString()
            ?? throw new FormatException("global.json states no SDK version.");
    }

    internal static string MajorMinor(string version)
    {
        var match = Regex.Match(version, @"([0-9]+)\.([0-9]+)");

        return match.Success
            ? $"{match.Groups[1].Value}.{match.Groups[2].Value}"
            : throw new FormatException($"No major.minor version in '{version}'.");
    }

    internal static string FeatureBand(string sdkVersion)
    {
        var match = Regex.Match(sdkVersion, @"^([0-9]+)\.([0-9]+)\.([0-9])[0-9]{2}$");

        return match.Success
            ? $"{match.Groups[1].Value}.{match.Groups[2].Value}.{match.Groups[3].Value}xx"
            : throw new FormatException($"'{sdkVersion}' is not a three-part SDK version.");
    }

    internal static IReadOnlyList<string> Occurrences(string text, string pattern) =>
        Regex.Matches(text, pattern).Select(match => match.Value).ToArray();
}
