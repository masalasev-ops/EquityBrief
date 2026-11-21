using System.Text.RegularExpressions;
using Xunit;

namespace EquityBrief.Tests.Checks;

// banned-prose, from 24.18: what picks stocks each night is the AI stock picker, and the old name names only the monitor
// that reviews it. Over the shipped source, the specs and their rules files, the guide and the decisions standing, every
// sentence holding the old name names the monitor, its review or the rename; in the documents the operator's words in quotes
// and a superseded decision's name keep theirs, and in the source nothing is set aside.
// see: What picks stocks each night is the AI stock picker, and the AI judge names only the monitor that reviews it
public partial class BannedProse
{
    static readonly string OldName = "AI " + "judge";

    // The words a sentence naming the monitor carries: the monitor itself, its review, or the rename.
    static readonly Regex Monitor = new(@"\b(monitor|review\w*|renamed)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // The operator's words in quotes, and a superseded decision's name after "Supersedes".
    static readonly Regex SetAside = new("\"[^\"\\n]*\"|Supersedes \\*\\*[^*]+\\*\\*", RegexOptions.CultureInvariant);

    // A sentence ends at a full stop, a semicolon, a line's end or a cell's edge.
    static readonly Regex Sentence = new(@"[^.;\n|<>]+", RegexOptions.CultureInvariant);

    internal static IReadOnlyList<string> OldNameUsedForThePicker(string text, bool quotesSetAside)
    {
        var read = quotesSetAside ? SetAside.Replace(text, " ") : text;

        return [.. Sentence.Matches(read)
            .Select(match => match.Value)
            .Where(sentence => sentence.Contains(OldName, StringComparison.Ordinal) && !Monitor.IsMatch(sentence))
            .Select(sentence => sentence.Trim())];
    }

    // The decisions standing: DECISIONS.md before its "Previously decided", which keeps a superseded entry's words whole.
    static string DecisionsStanding()
    {
        var decisions = Corpus.ReadAlone("docs/DECISIONS.md");
        var cut = decisions.IndexOf("\n## Previously decided", StringComparison.Ordinal);

        Assert.True(cut > 0, "DECISIONS.md holds no Previously decided section.");

        return decisions[..cut];
    }

    [Fact]
    public void TheOldNameOfTheAiStockPickerNamesOnlyTheMonitorInTheSourceTheSpecsTheGuideAndTheDecisionsStanding()
    {
        var source = Repository.SourceFiles()
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}EquityBrief.Tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();
        string[] documents = [.. Corpus.SpecsAndRules, "docs/HOW_IT_WORKS.html"];

        Assert.True(source.Length >= 300, $"Read {source.Length} shipped source file(s), expected at least 300.");

        var found = source
            .SelectMany(path => OldNameUsedForThePicker(File.ReadAllText(path), quotesSetAside: false).Select(sentence => $"{Path.GetRelativePath(Repository.Root, path)}: {sentence}"))
            .Concat(documents.SelectMany(document => OldNameUsedForThePicker(Corpus.ReadAlone(document), quotesSetAside: true).Select(sentence => $"{document}: {sentence}")))
            .Concat(OldNameUsedForThePicker(DecisionsStanding(), quotesSetAside: true).Select(sentence => $"docs/DECISIONS.md: {sentence}"))
            .ToArray();

        Assert.True(found.Length == 0, $"The old name names the picker in: {string.Join(" | ", found)}");
    }

    [Fact]
    public void TheWordCheckFindsThePickerUnderItsOldNameAndPassesTheMonitors()
    {
        // A planted use in the source and in a document, each found; the monitor's sentences, the operator's quoted words and a
        // superseded decision's name passed.
        Assert.Single(OldNameUsedForThePicker("region.Append(\"The " + OldName + "'s picks tonight\");", quotesSetAside: false));
        Assert.Single(OldNameUsedForThePicker("The " + OldName + " picks up to five stocks each night.", quotesSetAside: true));
        Assert.Single(OldNameUsedForThePicker("<td>An " + OldName + " run under another model</td>", quotesSetAside: true));
        Assert.Empty(OldNameUsedForThePicker("The " + OldName + " reviews the AI stock picker each week.", quotesSetAside: true));
        Assert.Empty(OldNameUsedForThePicker("\"The " + OldName + "'s review\" sits under the Improvement loop.", quotesSetAside: false));
        Assert.Empty(OldNameUsedForThePicker("The operator said \"rename the " + OldName + "\" on that day.", quotesSetAside: true));
        Assert.Empty(OldNameUsedForThePicker("Supersedes **The " + OldName + " picks straight after the night**, moved below.", quotesSetAside: true));

        // In the source a quoted string is read as it is drawn, never set aside.
        Assert.Single(OldNameUsedForThePicker("\"The " + OldName + " picked no stock tonight.\"", quotesSetAside: false));
    }
}
