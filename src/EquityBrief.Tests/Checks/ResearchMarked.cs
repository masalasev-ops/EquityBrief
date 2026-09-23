using System.Text.RegularExpressions;

namespace EquityBrief.Tests.Checks;

// research-marked. Every phase 10 rule the architecture states says what it
// rests on, as evidence with a source it links to, as judgement with its
// reasoning, or as not settled by research with the open question it links to;
// every source row is cited and every link resolves.
//
// A rule is a paragraph carrying `data-phase="10"`, and every paragraph of the
// subsections phase 10 added carries it, so a rule cannot leave the population
// by leaving the attribute off.
public class ResearchMarked
{
    internal const string Evidence = "<b>Evidence:</b>";

    internal const string Judgement = "<b>Judgement:</b>";

    internal const string NotSettled = "<b>Not settled by research:</b>";

    // The subsections every paragraph of which is a phase 10 rule.
    //
    // Hand-kept and reconciled against the document in both directions below, because a list
    // nothing reconciles can narrow its own scope: a subsection taken out of it with the document
    // untouched left every test green, so a rule could stop being read with nothing failing. The
    // 10.0 pass predicted that mutation would survive and it did.
    internal static readonly string[] Subsections =
    [
        "10.1 The trend rule's versions",
        "10.2 Why the entry is a zone, and what the midpoint is for",
        "11.1 How tonight's list is chosen",
        "13.6 How a candidate is judged",
        "13.7 What the judging can and cannot show",
    ];

    // One subsection of the architecture, with the paragraphs it holds and how many of them are
    // marked as a phase 10 rule.
    internal sealed record Marked(string Heading, int Rules, int Paragraphs);

    internal sealed record Finding(string Rule, string Fault);

    internal static IReadOnlyList<string> Rules(string architecture) =>
        [.. Regex.Matches(architecture, @"<p data-phase=""10"">(.*?)</p>", RegexOptions.Singleline).Select(match => match.Groups[1].Value)];

    // Every paragraph inside a phase 10 subsection, whatever it carries.
    internal static IReadOnlyList<string> SubsectionParagraphs(string architecture, string heading)
    {
        var start = architecture.IndexOf($"<h3>{heading}</h3>", StringComparison.Ordinal);

        Assert.True(start >= 0, $"The architecture has no subsection '{heading}'.");

        return [.. ParagraphsFrom(architecture, start)];
    }

    // Every subsection the document holds, with its paragraph counts. Read from the document
    // rather than from a list, because the list is what this is reconciled against.
    internal static IReadOnlyList<Marked> Subsected(string architecture)
    {
        var read = new List<Marked>();

        foreach (Match heading in Regex.Matches(architecture, @"<h3>(.*?)</h3>", RegexOptions.Singleline))
        {
            var paragraphs = ParagraphsFrom(architecture, heading.Index);

            read.Add(new Marked(
                heading.Groups[1].Value,
                paragraphs.Count(paragraph => paragraph.StartsWith(@"<p data-phase=""10"">", StringComparison.Ordinal)),
                paragraphs.Count));
        }

        return read;
    }

    // The paragraphs from a heading to the next heading of either level, or to the end of the
    // document where it is the last one. The end is read off a match that returns zero when it
    // finds nothing, so the last subsection in the document would otherwise read as empty.
    static IReadOnlyList<string> ParagraphsFrom(string architecture, int start)
    {
        var after = start + 4;
        var next = Regex.Match(architecture[after..], "<h[23]>");
        var end = next.Success ? next.Index + after : architecture.Length;

        return [.. Regex.Matches(architecture[start..end], @"<p[ >].*?</p>", RegexOptions.Singleline).Select(match => match.Value)];
    }

    static IReadOnlySet<string> Ids(string architecture, string prefix) =>
        Regex.Matches(architecture, $@"<tr id=""({prefix}-[^""]+)""").Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    static IReadOnlyList<string> Links(string text, string prefix) =>
        [.. Regex.Matches(text, $@"href=""#({prefix}-[^""]+)""").Select(match => match.Groups[1].Value)];

    // The faults in one rule: no mark at all, or a mark that does not carry what
    // it is for. A segment runs from its mark to the next mark or the end.
    internal static IReadOnlyList<Finding> Faults(string rule, IReadOnlySet<string> sources, IReadOnlySet<string> questions)
    {
        var name = Regex.Replace(rule, "<[^>]+>", string.Empty);
        var label = name.Length > 70 ? name[..70] : name;
        var marks = Regex.Matches(rule, $"{Regex.Escape(Evidence)}|{Regex.Escape(Judgement)}|{Regex.Escape(NotSettled)}");

        if (marks.Count == 0)
        {
            return [new Finding(label, "carries no Evidence, Judgement or Not settled by research mark")];
        }

        var faults = new List<Finding>();

        for (var index = 0; index < marks.Count; index++)
        {
            var from = marks[index].Index + marks[index].Length;
            var to = index + 1 < marks.Count ? marks[index + 1].Index : rule.Length;
            var segment = rule[from..to];

            switch (marks[index].Value)
            {
                case Evidence:
                    var cited = Links(segment, "src");

                    if (cited.Count == 0)
                    {
                        faults.Add(new Finding(label, "marks Evidence and links no source"));
                    }

                    faults.AddRange(cited.Where(id => !sources.Contains(id)).Select(id => new Finding(label, $"cites {id}, which section 23 has no row for")));
                    break;

                case Judgement:
                    var words = Regex.Replace(segment, "<[^>]+>", string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

                    if (words < 6)
                    {
                        faults.Add(new Finding(label, $"marks Judgement with {words} word(s) of reasoning, fewer than 6"));
                    }

                    break;

                default:
                    var open = Links(segment, "open");

                    if (open.Count == 0)
                    {
                        faults.Add(new Finding(label, "marks Not settled by research and links no open question"));
                    }

                    faults.AddRange(open.Where(id => !questions.Contains(id)).Select(id => new Finding(label, $"links {id}, which section 22 has no open question for")));
                    break;
            }
        }

        return faults;
    }

    [Fact]
    public void EveryPhaseTenRuleSaysWhatItRestsOn()
    {
        var architecture = File.ReadAllText(Repository.Architecture);
        var rules = Rules(architecture);
        var sources = Ids(architecture, "src");
        var questions = Ids(architecture, "open");

        // Stated in advance: 23 rules, 44 sources and 9 open questions when the
        // check was written, and each only grows while phase 10's rules stand.
        Assert.True(rules.Count >= 23, $"Read {rules.Count} phase 10 rules, expected at least 23.");
        Assert.True(sources.Count >= 44, $"Read {sources.Count} source rows, expected at least 44.");
        Assert.True(questions.Count >= 9, $"Read {questions.Count} open questions, expected at least 9.");

        var faults = rules.SelectMany(rule => Faults(rule, sources, questions)).ToArray();

        Assert.True(faults.Length == 0, string.Join("\n", faults.Select(fault => $"'{fault.Rule}' {fault.Fault}.")));
    }

    [Fact]
    public void EveryParagraphOfAPhaseTenSubsectionIsARule()
    {
        var architecture = File.ReadAllText(Repository.Architecture);

        foreach (var heading in Subsections)
        {
            var paragraphs = SubsectionParagraphs(architecture, heading);

            Assert.True(paragraphs.Count >= 1, $"'{heading}' holds no paragraph.");
            Assert.All(paragraphs, paragraph => Assert.StartsWith(@"<p data-phase=""10"">", paragraph, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void TheSubsectionListIsWhatTheDocumentSaysItIsInBothDirections()
    {
        var architecture = File.ReadAllText(Repository.Architecture);
        var subsected = Subsected(architecture);

        // Stated in advance. The count of subsections is context and carries a floor far below
        // what the document holds; the two counts below carry the property and are stated exactly,
        // because a reconciliation that found no fully marked subsection would pass an empty list.
        Assert.True(subsected.Count >= 30, $"Read {subsected.Count} subsections, expected at least 30.");

        var whole = subsected.Where(one => one.Paragraphs > 0 && one.Rules == one.Paragraphs).ToArray();
        var partly = subsected.Where(one => one.Rules > 0 && one.Rules < one.Paragraphs).ToArray();

        Assert.Equal(5, whole.Length);
        Assert.True(partly.Length == 1, $"Read {partly.Length} partly marked subsection(s), expected exactly 1.");

        // Both directions in one equality: a subsection every paragraph of which is a rule is in
        // the list, and every entry in the list is such a subsection. A heading taken out of the
        // list with the document untouched fails here, which is the mutation the 10.0 entry
        // recorded as surviving.
        Assert.Equal(
            Subsections.OrderBy(heading => heading, StringComparer.Ordinal),
            whole.Select(one => one.Heading).OrderBy(heading => heading, StringComparer.Ordinal));

        // The one subsection that carries rules and is not all rules, named with its counts rather
        // than left out by an absence. A section that stopped being partly marked, either by
        // marking the rest or by losing the mark, changes the population this list is read over.
        var run = Assert.Single(partly);

        Assert.Equal(("15.10 Run", 1, 5), (run.Heading, run.Rules, run.Paragraphs));
        Assert.DoesNotContain(run.Heading, Subsections);
    }

    [Fact]
    public void TheReaderReadsASubsectionToTheNextHeadingAndTheLastOneToTheEnd()
    {
        // The permanent proof under the reader above, which is where its two cases are: a
        // subsection ends at the next heading of either level, and the last subsection in a
        // document has no heading after it and runs to the end.
        const string document =
            "<h2>1 A</h2><p>outside</p>" +
            "<h3>1.1 B</h3><p data-phase=\"10\">one</p><p>two</p>" +
            "<h3>1.2 C</h3><p data-phase=\"10\">one</p>" +
            "<h2>2 D</h2><p>after</p>" +
            "<h3>2.1 E</h3><p data-phase=\"10\">one</p><p data-phase=\"10\">two</p>";

        Assert.Equal(
            [("1.1 B", 1, 2), ("1.2 C", 1, 1), ("2.1 E", 2, 2)],
            Subsected(document).Select(one => (one.Heading, one.Rules, one.Paragraphs)));
    }

    [Fact]
    public void EverySourceIsCitedAndEveryOpenQuestionIsLinked()
    {
        var architecture = File.ReadAllText(Repository.Architecture);
        var cited = Links(architecture, "src").ToHashSet(StringComparer.Ordinal);
        var linked = Links(architecture, "open").ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain(Ids(architecture, "src"), id => !cited.Contains(id));
        Assert.DoesNotContain(Ids(architecture, "open"), id => !linked.Contains(id));
        Assert.DoesNotContain(cited, id => !Ids(architecture, "src").Contains(id));
        Assert.DoesNotContain(linked, id => !Ids(architecture, "open").Contains(id));
    }

    [Fact]
    public void TheReaderFindsEachFaultAndPassesAWellFormedRule()
    {
        var sources = new HashSet<string>(StringComparer.Ordinal) { "src-a" };
        var questions = new HashSet<string>(StringComparer.Ordinal) { "open-b" };

        Assert.Empty(Faults(
            $"A rule. {Evidence} <a href=\"#src-a\">A (2000)</a>. {Judgement} the reason is stated here in words. {NotSettled} the margin (<a href=\"#open-b\">b</a>).",
            sources,
            questions));

        Assert.Contains("carries no", Assert.Single(Faults("A rule with nothing it rests on.", sources, questions)).Fault, StringComparison.Ordinal);
        Assert.Contains("links no source", Assert.Single(Faults($"A rule. {Evidence} a study said so.", sources, questions)).Fault, StringComparison.Ordinal);
        Assert.Contains("no row for", Assert.Single(Faults($"A rule. {Evidence} <a href=\"#src-z\">Z (1999)</a>.", sources, questions)).Fault, StringComparison.Ordinal);
        Assert.Contains("fewer than 6", Assert.Single(Faults($"A rule. {Judgement} because.", sources, questions)).Fault, StringComparison.Ordinal);
        Assert.Contains("links no open question", Assert.Single(Faults($"A rule. {NotSettled} nobody knows.", sources, questions)).Fault, StringComparison.Ordinal);
        Assert.Contains("no open question for", Assert.Single(Faults($"A rule. {NotSettled} <a href=\"#open-z\">z</a>.", sources, questions)).Fault, StringComparison.Ordinal);

        // A segment ends at the next mark, so a source linked under Judgement does not
        // stand for the Evidence mark before it.
        Assert.Contains("links no source", Assert.Single(Faults(
            $"A rule. {Evidence} none here. {Judgement} the reason is <a href=\"#src-a\">A (2000)</a> and more words follow.",
            sources,
            questions)).Fault, StringComparison.Ordinal);
    }
}
