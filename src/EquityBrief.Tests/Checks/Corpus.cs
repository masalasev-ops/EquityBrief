using System.Net;
using System.Text.RegularExpressions;

namespace EquityBrief.Tests.Checks;

internal sealed record CorpusFinding(string File, int Line, string Detail);

// The corpus, as the checks over it read it.
internal static class Corpus
{
    internal static readonly string[] Specs =
        ["CLAUDE.md", "docs/ARCHITECTURE.html", "docs/SCHEMA.md", "docs/BUILD_PLAN.md", "docs/RUNBOOK.md"];

    internal static readonly string[] Records =
        ["docs/DECISIONS.md", "docs/PROGRESS.md", "docs/CHANGELOG.md"];

    internal static IReadOnlyList<string> Documents => Specs.Concat(Records).ToArray();

    internal static string Read(string relative) =>
        File.ReadAllText(Path.Combine(Repository.Root, relative.Replace('/', Path.DirectorySeparatorChar)));

    // Every decision name, in document order, and which of them sit under
    // "Previously decided".
    internal static IReadOnlyList<string> DecisionNames(string decisions) =>
        Regex.Matches(decisions, @"^\*\*([^*]+)\*\*", RegexOptions.Multiline)
            .Select(match => match.Groups[1].Value)
            .ToArray();

    internal static IReadOnlyList<string> SupersededNames(string decisions)
    {
        var marker = decisions.IndexOf("## Previously decided", StringComparison.Ordinal);

        return marker < 0 ? [] : DecisionNames(decisions[marker..]);
    }

    // A citation is the same string in a document and in code, so one reader
    // covers both. The patterns are assembled from parts so this file never
    // contains the form it looks for, which would otherwise need an exemption
    // written down somewhere and remembered.
    //
    // The document form is parenthesised. The code form is a comment whose whole
    // content is the citation, which is what keeps prose describing the form
    // from reading as a use of it.
    //
    // Two markers, assembled the same way for the same reason. A decision
    // citation names a decision; an obligation citation names a row in
    // BUILD_PLAN's carried obligations table.
    internal const string Decision = "see";
    internal const string Obligation = "owes";

    static string InADocument(string marker) => @"\(" + marker + @": ([^)]+)\)";

    static string InCode(string marker) => @"^\s*(//\s*)?" + marker + @": (.+?)\s*(-->)?\s*$";

    internal static IReadOnlyList<CorpusFinding> Citations(string text, string file) =>
        Citations(Decision, text, file);

    // One reader for both markers rather than two readers that drift. The
    // marker is passed in, so this file still never contains either form.
    internal static IReadOnlyList<CorpusFinding> Citations(string marker, string text, string file)
    {
        var found = new List<CorpusFinding>();
        var lines = text.Split((char)10);

        for (var index = 0; index < lines.Length; index++)
        {
            foreach (Match match in Regex.Matches(lines[index], InADocument(marker)))
            {
                // Decoded, because a citation inside ARCHITECTURE.html is HTML.
                // The one decision whose name carries an ampersand could not be
                // cited from that document at all before this: written properly
                // as an entity it resolved to nothing, and written raw it would
                // have put invalid markup in the document to satisfy a reader.
                // Decoding is a no-op for every name that carries no entity.
                found.Add(new CorpusFinding(
                    file,
                    index + 1,
                    WebUtility.HtmlDecode(match.Groups[1].Value).Trim()));
            }

            var comment = Regex.Match(lines[index], InCode(marker));

            if (comment.Success)
            {
                found.Add(new CorpusFinding(file, index + 1, comment.Groups[2].Value.Trim()));
            }
        }

        return found;
    }

    internal static IReadOnlyList<string> SourceAndDocuments()
    {
        var files = new List<string>(Documents.Select(document =>
            Path.Combine(Repository.Root, document.Replace('/', Path.DirectorySeparatorChar))));

        files.AddRange(Repository.SourceFiles());
        files.AddRange(Repository.ProjectFiles());

        return files;
    }
}
