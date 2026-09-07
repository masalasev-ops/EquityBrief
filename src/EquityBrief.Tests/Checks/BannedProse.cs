using System.Text.RegularExpressions;

namespace EquityBrief.Tests.Checks;

// banned-prose. No file in the corpus or the shipped source carries the banned
// string or any form of it, and no file carries an em dash.
//
// The string is assembled from parts so this file is not itself an occurrence,
// which keeps the exemption to the one line that has to name it.
public class BannedProse
{
    static readonly string Banned = "hon" + "est";

    // The single exemption: the sentence in CLAUDE.md's Prose convention that
    // states the rule, matched on its opening rather than on the string itself.
    const string TheExemptSentence = "One word is banned outright across the corpus and in chat";

    static IReadOnlyList<CorpusFinding> Occurrences(string pattern, bool exemptTheRule)
    {
        var found = new List<CorpusFinding>();

        foreach (var file in Corpus.SourceAndDocuments().Concat(Repository.ToolScripts()))
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
        var scanned = Corpus.SourceAndDocuments().Concat(Repository.ToolScripts()).Count();

        Assert.True(scanned >= 40, $"Scanned {scanned} files, expected at least 40.");
        Assert.Empty(Occurrences(Banned, exemptTheRule: true));
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
        Assert.Empty(Occurrences(((char)0x2014).ToString(), exemptTheRule: false));
    }

    [Fact]
    public void TheReaderWouldFindOneIfThereWere()
    {
        // The permanent proof that the scan can fail.
        Assert.Matches(Banned, "this sentence is " + Banned);
        Assert.Matches(((char)0x2014).ToString(), "a dash " + (char)0x2014 + " here");
    }
}
