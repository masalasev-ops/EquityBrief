using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Checks;

// stated-counts. Every count a spec states about itself matches the derived
// count. Record entries are dated measurements and are exempt, so only the
// specs are read.
public class StatedCounts
{
    [Fact]
    public void TheDocumentLifecycleCountsItsOwnRows()
    {
        var rules = Corpus.Read("CLAUDE.md");
        var table = rules[rules.IndexOf("| Document | Kind | Rule |", StringComparison.Ordinal)..];
        var rows = table.Split((char)10).TakeWhile(line => line.StartsWith('|')).ToArray();

        Assert.Contains("Five specs and three records", rules, StringComparison.Ordinal);
        Assert.Equal(5, rows.Count(row => row.Contains("| spec |", StringComparison.Ordinal)));
        Assert.Equal(3, rows.Count(row => row.Contains("| record |", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheDefinitionOfDoneCountsItsOwnConditions()
    {
        // The stated count is read out of the sentence rather than repeated
        // here. It was written as a literal 7 beside a literal "All seven",
        // which is the defect this check exists to find, in the check itself: a
        // condition added to the list meant editing the rules, this assertion
        // and its anchor string, and only the first of the three is the fact.
        var rules = Corpus.Read("CLAUDE.md");
        var stated = Regex.Match(rules, @"All (\w+), or it is not done");

        Assert.True(stated.Success, "CLAUDE.md no longer states how many done conditions there are.");
        Assert.True(
            Numbers.ContainsKey(stated.Groups[1].Value),
            $"CLAUDE.md states 'All {stated.Groups[1].Value}' done conditions, which is not a number word this check reads.");

        var expected = Numbers[stated.Groups[1].Value];
        var section = rules[stated.Index..];

        var conditions = section.Split((char)10)
            .SkipWhile(line => !line.StartsWith("1. ", StringComparison.Ordinal))
            .TakeWhile(line => Regex.IsMatch(line, @"^\d+\. ") || line.StartsWith("   ", StringComparison.Ordinal))
            .Count(line => Regex.IsMatch(line, @"^\d+\. "));

        Assert.Equal(expected, conditions);

        // And the count is stated the same way wherever else the rules give it,
        // because the merge section states it a second time and a list that grew
        // would otherwise leave one of the two behind.
        Assert.Contains($"all {stated.Groups[1].Value} done conditions", rules, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLayoutBlockCountsTheProjects()
    {
        // The count is stated in the layout block's own line for the solution
        // file and derived from the block beneath it and from the disk. It was
        // asserted against BUILD_PLAN.md until that document's phase 0 detail
        // was removed, which is a third statement of one fact and the one that
        // drifted: the stated count belongs in the document that carries the
        // block, not in the one that used to describe the checkpoint building it.
        var rules = Corpus.Read("CLAUDE.md");
        var block = rules.Split("```" + (char)10)[1];
        var listed = Regex.Matches(block, @"^\s+(EquityBrief\.[A-Za-z]+)\s", RegexOptions.Multiline).Count;

        Assert.Contains("the six projects", rules, StringComparison.Ordinal);
        Assert.Equal(6, listed);
        Assert.Equal(6, Repository.ProjectFiles().Count);
    }

    [Fact]
    public void TheArchitectureCountsItsOwnReasons()
    {
        var table = ArchitectureTables
            .In(File.ReadAllText(Repository.Architecture))
            .Single(candidate => candidate.Heading == "11. The shortlist and its six reasons");

        Assert.Equal(6, table.Body.Count);
    }

    // Section 1 breaks the shortlist's conditions into three parts and states
    // their total. That makes it the third place the count lives, after section
    // 11's heading and section 11's rows, so it is the third place it can drift
    // from them. Read as a total and as a sum, because a sentence can disagree
    // with the table either by naming a different total or by having parts that
    // do not add up to the one it names.
    static readonly Dictionary<string, int> Numbers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4,
        ["five"] = 5, ["six"] = 6, ["seven"] = 7, ["eight"] = 8,
    };

    internal static (int Stated, int Parts)? ConditionBreakdown(string text)
    {
        var match = Regex.Match(
            text,
            @"(\w+) of the (\w+) conditions detect [^.]*?, (\w+) detects [^.]*?, and (\w+) is a calendar fact");

        if (!match.Success || !Numbers.ContainsKey(match.Groups[2].Value))
        {
            return null;
        }

        int[] groups = [1, 3, 4];

        return groups.Any(group => !Numbers.ContainsKey(match.Groups[group].Value))
            ? null
            : (Numbers[match.Groups[2].Value], groups.Sum(group => Numbers[match.Groups[group].Value]));
    }

    [Fact]
    public void SectionOneCountsTheConditionsSectionElevenLists()
    {
        var architecture = File.ReadAllText(Repository.Architecture);

        var listed = ArchitectureTables.In(architecture)
            .Single(candidate => candidate.Heading == "11. The shortlist and its six reasons")
            .Body.Count;

        var breakdown = ConditionBreakdown(architecture);

        Assert.True(
            breakdown is not null,
            "Section 1 no longer states the conditions as a total broken into parts, so this check " +
            "reads nothing. A sentence that stopped stating the count is not a sentence that agrees " +
            "with the table.");

        Assert.Equal(listed, breakdown!.Value.Stated);
        Assert.Equal(listed, breakdown.Value.Parts);
    }

    [Fact]
    public void TheCheckReportsASentenceThatDisagreesWithTheTable()
    {
        // The permanent proof, over constructed sentences. The first agrees,
        // the second names a total the parts do not reach, and the third stops
        // stating the count at all, which reads as nothing rather than as a pass.
        var agrees = ConditionBreakdown(
            "Four of the six conditions detect arrival at a price, one detects a break on unusual volume, and one is a calendar fact");

        Assert.Equal((6, 6), agrees);

        var doesNot = ConditionBreakdown(
            "Four of the seven conditions detect arrival at a price, one detects a break on unusual volume, and one is a calendar fact");

        Assert.Equal(7, doesNot!.Value.Stated);
        Assert.Equal(6, doesNot.Value.Parts);
        Assert.NotEqual(doesNot.Value.Stated, doesNot.Value.Parts);

        Assert.Null(ConditionBreakdown("the conditions are described in section 11"));
    }

    [Fact]
    public void TheMarkVocabularyCountsItsOwnMarks()
    {
        // Section 15.5 opens by stating how many marks there are, and the count
        // is read out of that sentence rather than repeated here.
        //
        // This is also what holds contradiction F's resolution in place. The
        // Level chart row names four elements drawn at three different points,
        // and the harness reads that one row as four claims rather than the
        // document carrying four rows. The obvious wrong repair is to split the
        // row, which would turn one mark into four in a vocabulary whose point
        // is that a mark is defined once, and would leave the opening sentence
        // saying seven over a table of ten. Now it fails instead.
        var architecture = File.ReadAllText(Repository.Architecture);
        var stated = Regex.Match(architecture, @"<p>(\w+) marks\.");

        Assert.True(stated.Success, "Section 15.5 no longer states how many marks there are.");
        Assert.True(
            Numbers.ContainsKey(stated.Groups[1].Value),
            $"Section 15.5 states '{stated.Groups[1].Value} marks', which is not a number word this check reads.");

        var table = ArchitectureTables.In(architecture)
            .Single(candidate => candidate.Heading == "15.5 The mark vocabulary");

        Assert.Equal(Numbers[stated.Groups[1].Value], table.Body.Count);
    }

    [Fact]
    public void TheCheckWouldNoticeADisagreement()
    {
        // The permanent proof: the derivation is a real count of real rows, so
        // a different table yields a different number.
        var tables = ArchitectureTables.In(File.ReadAllText(Repository.Architecture));

        Assert.NotEqual(
            tables.Single(candidate => candidate.Heading == "11. The shortlist and its six reasons").Body.Count,
            tables.Single(candidate => candidate.Heading == "7. Component catalogue").Body.Count);
    }
}
