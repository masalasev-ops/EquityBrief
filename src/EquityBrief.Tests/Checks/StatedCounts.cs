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
        var rules = Corpus.Read("CLAUDE.md");
        var section = rules[rules.IndexOf("All seven, or it is not done", StringComparison.Ordinal)..];

        var conditions = section.Split((char)10)
            .SkipWhile(line => !line.StartsWith("1. ", StringComparison.Ordinal))
            .TakeWhile(line => Regex.IsMatch(line, @"^\d+\. ") || line.StartsWith("   ", StringComparison.Ordinal))
            .Count(line => Regex.IsMatch(line, @"^\d+\. "));

        Assert.Equal(7, conditions);
    }

    [Fact]
    public void TheLayoutBlockCountsTheProjects()
    {
        var block = Corpus.Read("CLAUDE.md").Split("```" + (char)10)[1];
        var listed = Regex.Matches(block, @"^\s+(EquityBrief\.[A-Za-z]+)\s", RegexOptions.Multiline).Count;

        Assert.Contains("Six projects", Corpus.Read("docs/BUILD_PLAN.md"), StringComparison.Ordinal);
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
