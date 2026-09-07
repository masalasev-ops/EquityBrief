using System.Text.RegularExpressions;

namespace EquityBrief.Tests.Checks;

// path-casing. Every file path appearing as a string literal in source matches
// the on-disk path exactly, byte for byte.
//
// This targets a fault neither of the operator's machines can see. Case
// sensitivity is a property of the filesystem, not the operating system:
// Windows and macOS are both insensitive by default and Linux is not, so a path
// written with the wrong case works on both development machines and fails on a
// runner.
public class PathCasing
{
    static IReadOnlyList<string> Entries()
    {
        var skip = new[] { ".git", "bin", "obj", "artifacts", "data" };

        return Directory
            .EnumerateFileSystemEntries(Repository.Root, "*", SearchOption.AllDirectories)
            .Where(entry => !entry.Split(Path.DirectorySeparatorChar).Any(part => skip.Contains(part)))
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    // A path candidate is a literal that carries a file extension, or one that
    // sits inside a Path.Combine call. Anything looser flags a JSON key called
    // properties because a directory happens to be called Properties, which is
    // a false positive that would teach everyone to ignore the check.
    internal static IReadOnlyList<string> LiteralsIn(string source)
    {
        var candidates = Regex
            .Matches(source, @"""([A-Za-z0-9_.\-]+\.[A-Za-z0-9]{1,6})""")
            .Select(match => match.Groups[1].Value)
            .ToList();

        foreach (Match call in Regex.Matches(source, @"Path\.Combine\(([^;]*?)\)"))
        {
            candidates.AddRange(Regex
                .Matches(call.Groups[1].Value, @"""([A-Za-z0-9_.\-]+)""")
                .Select(match => match.Groups[1].Value));
        }

        return candidates.Where(value => value.Length >= 3).Distinct(StringComparer.Ordinal).ToArray();
    }

    [Fact]
    public void EveryPathLiteralMatchesTheNameOnDisk()
    {
        var entries = Entries();
        var byLowercase = entries.ToLookup(name => name.ToLowerInvariant());

        var literals = Repository.SourceFiles()
            .SelectMany(file => LiteralsIn(File.ReadAllText(file)).Select(literal => (file, literal)))
            .ToArray();

        var resolved = literals.Where(pair => byLowercase.Contains(pair.literal.ToLowerInvariant())).ToArray();

        // Two scopes, and only the second carries the property. The number of
        // literals is a fact about how much source there is; the number that
        // name something on disk is what this check is about.
        Assert.True(literals.Length >= 30, $"Scanned {literals.Length} path literals, expected at least 30.");
        Assert.True(resolved.Length >= 10, $"Only {resolved.Length} name a repository entry, expected at least 10.");

        var miscased = resolved
            .Where(pair => !byLowercase[pair.literal.ToLowerInvariant()].Contains(pair.literal, StringComparer.Ordinal))
            .Select(pair => $"{Path.GetFileName(pair.file)}: {pair.literal}")
            .ToArray();

        Assert.Empty(miscased);
    }

    [Fact]
    public void TheCheckReportsAMiscasedLiteral()
    {
        // The permanent proof. CLAUDE.md exists; claude.md does not, on a
        // filesystem that tells the difference.
        var entries = Entries();

        Assert.Contains("CLAUDE.md", entries, StringComparer.Ordinal);
        // Built rather than written, so this file is not itself a miscased literal.
        Assert.DoesNotContain("CLAUDE.md".ToLowerInvariant(), entries, StringComparer.Ordinal);
        Assert.Contains("CLAUDE.md", LiteralsIn("Path.Combine(Root, \"CLAUDE.md\")"), StringComparer.Ordinal);
    }
}
