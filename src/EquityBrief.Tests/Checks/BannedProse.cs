using System.Text.RegularExpressions;

namespace EquityBrief.Tests.Checks;

// banned-prose. No file in the corpus or the shipped source carries the banned
// string or any form of it, and no file carries an em dash.
//
// The string is assembled from parts so this file is not itself an occurrence,
// which keeps the exemption to the one line that has to name it.
//
// The scan reads every text file the repository tracks. It read the eight
// documents, the source and project files and the scripts in tools until 0.7's
// review, and therefore not the workflow, src/Directory.Build.props,
// EquityBrief.slnx or the two files in fixtures. Nothing in those carried
// either pattern, so it was a check narrower than it read rather than a live
// fault, and that is the survivorship shape the Checks section argues about: a
// check that silently narrows its own scope keeps passing.
public class BannedProse
{
    static readonly string Banned = "hon" + "est";

    static readonly string EmDash = ((char)0x2014).ToString();

    // The single exemption: the sentence in CLAUDE.md's Prose convention that
    // states the rule, matched on its opening rather than on the string itself.
    const string TheExemptSentence = "One word is banned outright across the corpus and in chat";

    // Every text file git tracks, which is every file in the repository that is
    // not gitignored, less the captured provider responses. A file carrying a
    // zero byte is not prose and is counted separately rather than read as
    // though it were.
    //
    // The captures are excluded because they are not prose this repository
    // writes. A rule about how this corpus is written cannot govern bytes a
    // provider sent, and the only way to satisfy it over them would be to edit
    // the provider's text, which would make the file no longer a capture: the
    // manifest schema says in so many words that what may never be trimmed is
    // the shape. Found at 1.7, when a news article's own text carried an em
    // dash. The three earlier captures happened to carry neither pattern, so
    // this was a rule that had not yet met the thing it could not govern.
    //
    // The exclusion is the captured files and nothing else. The manifest, the
    // README and anything under expectations/ are written here and are scanned,
    // which the next test asserts rather than leaves to the reader.
    static IReadOnlyList<string> Scanned() =>
        Repository.TrackedFiles()
            .Where(file => !IsCapture(file))
            .Where(file => !File.ReadAllBytes(file).Contains((byte)0))
            .ToArray();

    // A captured provider response: a .json file inside a fixture folder that
    // some manifest names as an input.
    internal static bool IsCapture(string file)
    {
        if (!file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var folder = Path.GetDirectoryName(file);
        var manifest = folder is null ? null : Path.Combine(folder, "manifest.json");

        if (manifest is null || !File.Exists(manifest) || string.Equals(file, manifest, StringComparison.Ordinal))
        {
            return false;
        }

        // Named by the manifest, so a json file dropped into a fixture folder
        // and never declared is still scanned. The exclusion follows the
        // declaration rather than the folder.
        return File.ReadAllText(manifest)
            .Contains($"\"{Path.GetFileName(file)}\"", StringComparison.Ordinal);
    }

    static IReadOnlyList<CorpusFinding> Occurrences(string pattern, bool exemptTheRule) =>
        Occurrences(pattern, exemptTheRule, Scanned());

    static IReadOnlyList<CorpusFinding> Occurrences(
        string pattern,
        bool exemptTheRule,
        IReadOnlyList<string> files)
    {
        var found = new List<CorpusFinding>();

        foreach (var file in files)
        {
            var lines = File.ReadAllText(file).Split((char)10);

            for (var index = 0; index < lines.Length; index++)
            {
                if (exemptTheRule && lines[index].Contains(TheExemptSentence, StringComparison.Ordinal))
                {
                    continue;
                }

                if (Regex.IsMatch(lines[index], pattern, RegexOptions.IgnoreCase))
                {
                    found.Add(new CorpusFinding(file, index + 1, lines[index].Trim()));
                }
            }
        }

        return found;
    }

    [Fact]
    public void TheBannedStringAppearsOnlyWhereTheRuleNamesIt()
    {
        var tracked = Repository.TrackedFiles();
        var scanned = Scanned();

        // Two scopes, stated in advance. The tracked count is context; the
        // scanned count is the population carrying the property, and its floor
        // sits far enough below the count that ordinary growth never moves it.
        Assert.True(tracked.Count >= 80, $"git tracks {tracked.Count} files, expected at least 80.");
        Assert.True(scanned.Count >= 80, $"Scanned {scanned.Count} text files, expected at least 80.");

        Assert.Empty(Occurrences(Banned, exemptTheRule: true, scanned));
    }

    [Fact]
    public void TheExemptSentenceIsStillThereAndStillCarriesIt()
    {
        // If the rule's own sentence moved or lost the string, the exemption
        // above would be exempting nothing and the check would have quietly
        // narrowed its own scope.
        var rules = Corpus.Read("CLAUDE.md");
        var sentence = rules.Split((char)10)
            .Single(line => line.Contains(TheExemptSentence, StringComparison.Ordinal));

        Assert.Contains(Banned, sentence, StringComparison.OrdinalIgnoreCase);
        Assert.Single(Occurrences(Banned, exemptTheRule: false));
    }

    [Fact]
    public void NoFileCarriesAnEmDash()
    {
        Assert.Empty(Occurrences(EmDash, exemptTheRule: false));
    }

    [Fact]
    public void AFileCarryingEitherPatternIsFound()
    {
        // The permanent proof, over files rather than over a string in memory.
        // The scan reads files, and a proof against a string never exercises
        // the reader that opens them. Both planted files sit outside the
        // repository, because a planted one inside it would be tracked.
        using var elsewhere = new TemporaryDirectory();

        var withTheString = Path.Combine(elsewhere.Path, "planted-string.txt");
        var withTheDash = Path.Combine(elsewhere.Path, "planted-dash.txt");

        File.WriteAllText(withTheString, "a clean line" + (char)10 + "this sentence is " + Banned + (char)10);
        File.WriteAllText(withTheDash, "a clean line" + (char)10 + "a dash " + EmDash + " here" + (char)10);

        var planted = new[] { withTheString, withTheDash };

        var strings = Occurrences(Banned, exemptTheRule: false, planted);
        var dashes = Occurrences(EmDash, exemptTheRule: false, planted);

        Assert.Single(strings);
        Assert.Equal(2, strings[0].Line);
        Assert.Single(dashes);
        Assert.Equal(2, dashes[0].Line);
    }

    [Fact]
    public void TheExemptionAppliesToTheRulesSentenceAndNothingElse()
    {
        // A file whose text is the exempt sentence with the string on another
        // line still fails, so the exemption cannot be borrowed by putting the
        // sentence at the top of a document.
        using var elsewhere = new TemporaryDirectory();

        var borrowed = Path.Combine(elsewhere.Path, "borrowed.txt");

        File.WriteAllText(
            borrowed,
            TheExemptSentence + " and the string is " + Banned + (char)10
            + "and this line is " + Banned + (char)10);

        Assert.Single(Occurrences(Banned, exemptTheRule: true, [borrowed]));
    }

    [Fact]
    public void TheCaptureExclusionIsTheCapturesAndNothingElse()
    {
        // The permanent proof under the exclusion, in both directions. An
        // exclusion nobody bounds is how a check quietly stops covering the
        // thing it was written for.
        var root = Path.Combine(Repository.Root, "fixtures", "membership-2026-09-05");

        // Excluded: a json file the manifest names as an input.
        Assert.True(IsCapture(Path.Combine(root, "news-2026-09-08.json")));
        Assert.True(IsCapture(Path.Combine(root, "bars-AAPL.json")));

        // Scanned: the manifest itself, the folder's README, and a json file in
        // the folder that no manifest declares.
        Assert.False(IsCapture(Path.Combine(root, "manifest.json")));
        Assert.False(IsCapture(Path.Combine(Repository.Root, "fixtures", "README.md")));
        Assert.False(IsCapture(Path.Combine(root, "undeclared.json")));
        Assert.False(IsCapture(Path.Combine(Repository.Root, "source-lists.json")));

        // And the scan still reads the corpus. The count is stated because an
        // exclusion that had taken the whole population would leave every
        // assertion above true and every assertion about prose vacuous.
        var scanned = Scanned();

        Assert.True(scanned.Count >= 40, $"Scanned {scanned.Count} tracked text files, expected at least 40.");
        Assert.Contains(scanned, file => file.EndsWith("CLAUDE.md", StringComparison.Ordinal));
        Assert.Contains(scanned, file => file.EndsWith("manifest.json", StringComparison.Ordinal));

        // The excluded set is small and named, rather than whatever happened to
        // be in a folder. Nine captured inputs today across one fixture.
        var excluded = Repository.TrackedFiles().Count(IsCapture);

        Assert.True(excluded is >= 5 and <= 30, $"Excluded {excluded} captured responses, expected between 5 and 30.");
    }
}
