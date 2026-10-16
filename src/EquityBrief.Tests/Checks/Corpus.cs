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

    // A record's earlier entries, moved whole into a file of their own that takes no new entry, and read with the
    // live file as one record wherever the record is read. Before says where they stand in it: before the live
    // file's entries, as the build record's oldest do, or after them, as the changelog's do beneath its newest.
    // see: A record's earlier entries move whole to an archive that takes no new entry and is read with its live file as one record
    internal sealed record Archive(string Record, string File, bool Before);

    internal static readonly Archive[] Archives =
    [
        new("docs/PROGRESS.md", "docs/archive/PROGRESS-phases-0-to-18.md", Before: true),
        new("docs/CHANGELOG.md", "docs/archive/CHANGELOG-to-2026-10-11.md", Before: false),
    ];

    internal static Archive? ArchiveOf(string record) =>
        Archives.FirstOrDefault(archive => string.Equals(archive.Record, record, StringComparison.Ordinal));

    // The words each archive's opening line carries beside its live file's path.
    internal const string TakesNoNewEntry = "This file takes no new entry.";

    // The path-scoped rules, read from the directory rather than listed, so a
    // fifth rules file cannot be added outside every check at once. A list here
    // would be a second statement of what the directory holds, and the whole
    // point of the directory is that a session loads what its paths match.
    //
    // It refuses rather than returning what it found, because a directory read
    // is the one population shape that fails silently: an empty result leaves
    // every assertion over it of the "none of them disagreed" form, and the run
    // is green having read nothing. The floor guards a read that has emptied and
    // is not a count of the files, which `stated-counts` holds against CLAUDE.md.
    //
    // Repo-relative with forward slashes, which is the spelling `git show
    // --numstat` hands `changelog-reconciles` and the spelling `Read` turns into
    // a platform path. A `Path.Combine` here would be a backslash on Windows and
    // would match nothing in the history, green on one machine and right on the
    // other.
    internal const int RulesFloor = 4;

    internal static IReadOnlyList<string> Rules => RulesIn(Path.Combine(Repository.Root, ".claude", "rules"));

    internal static IReadOnlyList<string> RulesIn(string directory)
    {
        var found = Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.md")
                .Select(path => ".claude/rules/" + Path.GetFileName(path))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray()
            : [];

        if (found.Length < RulesFloor)
        {
            throw new InvalidOperationException(
                $"Read {found.Length} rules files from .claude/rules, expected at least {RulesFloor}. " +
                "A directory read that returned nothing would assert every property over an " +
                "empty set and report green having widened nothing, so this refuses instead.");
        }

        return found;
    }

    // The specs, with the rules files among them. One member rather than two,
    // because `pinned-constants` keys a dictionary on it and a path reachable
    // through two members would be two keys of one name.
    internal static IReadOnlyList<string> SpecsAndRules => [.. Specs, .. Rules];

    internal static IReadOnlyList<string> Documents => [.. Specs, .. Rules, .. Records, .. Archives.Select(archive => archive.File)];

    // A record with an archive is read whole, its archive's entries in their place, so no check reading it can read
    // the live file alone; every other file is read as it stands.
    internal static string Read(string relative) =>
        ArchiveOf(relative) is { } archive
            ? Joined(archive, ReadAlone(relative), ReadAlone(archive.File))
            : ReadAlone(relative);

    // One file as it stands, a live record without its archive among them.
    internal static string ReadAlone(string relative) =>
        File.ReadAllText(Path.Combine(Repository.Root, relative.Replace('/', Path.DirectorySeparatorChar)));

    // The record whole, from its live file and its archive: the live file without the line beneath its title naming
    // the archive, and the archive's entries without its opening line and the blank line after it, set before the
    // live file's first entry or after its last. A live file not naming its archive there, or an archive not opening
    // on one line naming its live file and saying it takes no new entry, is refused rather than read as a record.
    internal static string Joined(Archive archive, string live, string earlier)
    {
        var title = live.IndexOf('\n');
        var named = title < 0 ? -1 : live.IndexOf('\n', title + 1);

        if (named < 0 || !live[(title + 1)..named].Contains(archive.File, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{archive.Record} does not name {archive.File} on the line beneath its title.");
        }

        var opening = earlier.IndexOf("\n\n", StringComparison.Ordinal);
        var first = opening < 0 ? string.Empty : earlier[..opening];

        if (first.Length == 0
            || first.Contains('\n')
            || !first.Contains(archive.Record, StringComparison.Ordinal)
            || !first.EndsWith(TakesNoNewEntry, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{archive.File} does not open with one line naming {archive.Record} and saying it takes no new entry.");
        }

        var record = live[..(title + 1)] + live[(named + 1)..];
        var entries = earlier[(opening + 2)..];

        if (!archive.Before)
        {
            return record + entries;
        }

        var marker = record.IndexOf("\n## Entries\n", StringComparison.Ordinal);

        if (marker < 0)
        {
            throw new InvalidOperationException($"{archive.Record} holds no entries section to set its archive's entries in.");
        }

        var next = Regex.Match(record[marker..], "^### ", RegexOptions.Multiline);
        var at = next.Success ? marker + next.Index : record.Length;

        return record.Insert(at, entries);
    }

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

        // A quoted line in CHANGELOG.md is prior text, which that file's own
        // format section declares it to be. It is a record of what a document
        // said and not a statement this corpus makes, so a citation inside one
        // is a citation being reported rather than made.
        //
        // Without this, superseding a decision whose citation sits in a passage
        // the changelog has to quote makes no-superseded-citation red, and the
        // only ways out are worse than the fault: paraphrasing prior text that
        // the format requires verbatim, or editing a record that is append only.
        // The reader is what was wrong, and it was wrong in the direction this
        // harness names, reading a record of a thing as the thing.
        var record = IsChangelog(file);

        for (var index = 0; index < lines.Length; index++)
        {
            if (record && Regex.IsMatch(lines[index], @"^\s*>"))
            {
                continue;
            }

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

    // The changelog's live file and its archive, each of which quotes prior text.
    static bool IsChangelog(string file)
    {
        var path = file.Replace('\\', '/');

        return path.EndsWith("docs/CHANGELOG.md", StringComparison.Ordinal)
            || path.EndsWith(ArchiveOf("docs/CHANGELOG.md")!.File, StringComparison.Ordinal);
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
