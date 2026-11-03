using System.Text.RegularExpressions;
using Xunit;

namespace EquityBrief.Tests.Checks;

// see: The night stays at 23:00 UTC on the operator's word, and a night finding the day's file not yet posted still moves it later
public partial class NightlyCost
{
    [Fact]
    public void TheRunbooksInstantAndItsTwoSetupCommandsNameOneInstant()
    {
        var runbook = Corpus.Read("docs/RUNBOOK.md");
        var stated = Assert.Single(Regex.Matches(runbook, @"\*\*The instant is (\d\d:\d\d) UTC\.\*\*")).Groups[1].Value;

        // The registration and the move to weekdays, the two commands whose trigger the night's own task is handed, each read
        // from the night's first registered day as the other tasks' are not.
        var commands = Regex.Matches(runbook, @"\$trigger\.StartBoundary = '2026-09-11T(\d\d:\d\d):00Z'");

        Assert.Equal("23:00", stated);
        Assert.Equal(2, commands.Count);
        Assert.All(commands, command => Assert.Equal(stated, command.Groups[1].Value));

        Assert.Contains($"{stated} UTC is 19:00 or 18:00 in New York", runbook, StringComparison.Ordinal);
        Assert.Contains("reading it back shows `2026-09-10T19:00:00-04:00`", runbook, StringComparison.Ordinal);
        Assert.DoesNotContain("23:30", runbook, StringComparison.Ordinal);
    }
}
