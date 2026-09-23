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
    internal static readonly string[] Subsections =
    [
        "10.1 The trend rule's versions",
        "10.2 Why the entry is a zone, and what the midpoint is for",
        "11.1 How tonight's list is chosen",
        "13.6 How a candidate is judged",
        "13.7 What the judging can and cannot show",
    ];

    internal sealed record Finding(string Rule, string Fault);

    internal static IReadOnlyList<string> Rules(string architecture) =>
        [.. Regex.Matches(architecture, @"<p data-phase=""10"">(.*?)</p>", RegexOptions.Singleline).Select(match => match.Groups[1].Value)];

    // Every paragraph inside a phase 10 subsection, whatever it carries.
    internal static IReadOnlyList<string> SubsectionParagraphs(string architecture, string heading)
    {
        var start = architecture.IndexOf($"<h3>{heading}</h3>", StringComparison.Ordinal);

        Assert.True(start >= 0, $"The architecture has no subsection '{heading}'.");

        var end = Regex.Match(architecture[(start + 4)..], "<h[23]>").Index + start + 4;

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
