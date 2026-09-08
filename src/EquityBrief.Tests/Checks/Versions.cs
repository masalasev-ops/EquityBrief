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

    // Every occurrence across a set of documents, each carrying the document it
    // came from, so a disagreement names the file a person has to open rather
    // than only the value that was wrong.
    internal static IReadOnlyList<VersionMention> MentionsIn(
        IReadOnlyDictionary<string, string> documents,
        string pattern) =>
        documents
            .OrderBy(document => document.Key, StringComparer.Ordinal)
            .SelectMany(document => Regex
                .Matches(document.Value, pattern)
                .Select(match => new VersionMention(document.Key, match.Value)))
            .ToArray();

    // The comparison itself, and the population that carries it.
    //
    // A scan that compared nothing throws rather than returning an empty list,
    // because every assertion this feeds is "none of them disagreed", and none
    // of zero is true. That is the route by which a check reports green having
    // read nothing, and it is the shape of the shallow-clone fault the 0.7
    // addendum records, arriving from the other side.
    internal static IReadOnlyList<VersionMention> Disagreeing(
        IReadOnlyList<VersionMention> mentions,
        string expected,
        string what) =>
        mentions.Count > 0
            ? mentions.Where(mention => mention.Value != expected).ToArray()
            : throw new InvalidOperationException(
                $"No {what} was found in any document, so nothing was compared against '{expected}'. " +
                "A run that compares nothing must fail rather than pass over an empty scan.");
}

// One statement of a version in one document. The document is carried because
// the property is a comparison against the build, and a comparison that fails
// has to say which file states the value that disagrees.
internal sealed record VersionMention(string Document, string Value);
