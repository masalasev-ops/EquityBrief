using EquityBrief.Core.Facts;
using EquityBrief.Core.Research;

namespace EquityBrief.Tests.Checks;

// claim-admissibility, the 6.4 correction of 2026-10-11: a figure written as a number of times an earlier figure that the
// facts file holds only as a growth is refused, since a growth is the rise as a fraction of the earlier figure and 1.49 is
// a rise of 149 per cent, the later figure being about two and a half times the earlier one. CVX's segment commentary of
// 2026-10-11 wrote "rose to $3.54 billion from $1.42 billion, 1.50 times the year-earlier figure" and passed. Worked by
// hand over constructed facts.
// see: A growth is written as a rise in per cent and never as a number of times the earlier figure
public partial class ClaimAdmissibility
{
    // U.S. upstream earnings of $3.54 billion against $1.42 billion a year earlier: a growth of 3.54 / 1.42 - 1 = 1.492958.
    static readonly Fact[] GrowthFacts =
    [
        new("company name", "Chevron Corp", "membership"),
        new("segment U.S. Upstream Earnings 2026-06-30", "3540000000", "fundamental"),
        new("segment U.S. Upstream Earnings growth on a year earlier 2026-06-30", "1.492958", "fundamental"),
        new("earnings multiple tonight", "19.565", "fundamental reading"),
    ];

    static (string Offending, string Reason)[] Multiples(string section, string prose, IReadOnlyList<Fact> facts) =>
        [.. ClaimRules.Check(section, prose, facts, [RegionRelease]).Findings.Select(finding => (finding.Offending, finding.Reason))];

    [Fact]
    public void AGrowthWrittenAsANumberOfTimesTheEarlierFigureIsRefusedAndOneWrittenAsARiseInPerCentPasses()
    {
        // 1.49 rounds the growth and the facts file holds it as nothing else, so "1.49 times" says the later figure is
        // 1.49 times the earlier one where it is 2.49 times: refused, in either way of writing times.
        Assert.Equal(
            [("1.49", ClaimRules.GrowthAsAMultiple)],
            Multiples("The segment commentary", "U.S. upstream earnings rose to $3.54 billion, 1.49 times the year-earlier figure [N].", GrowthFacts));
        Assert.Equal(
            [("1.49x", ClaimRules.GrowthAsAMultiple)],
            Multiples("The segment commentary", "U.S. upstream earnings rose to $3.54 billion, 1.49x the year-earlier figure [N].", GrowthFacts));

        // The same growth as a rise in per cent: 1.492958 is 149.2958 per cent, which 149% rounds.
        Assert.Empty(Multiples("The segment commentary", "U.S. upstream earnings rose to $3.54 billion, up 149% on the year-earlier figure [N].", GrowthFacts));

        // A multiple the facts file holds as a multiple is written as one: 19.6 rounds the earnings multiple.
        Assert.Empty(Multiples(Ungoverned, "The shares trade at 19.6 times their trailing earnings [N].", GrowthFacts));

        // A figure the facts file also holds as something other than a growth passes, since the match is on existence and a
        // ratio of 1.49 may rightly be written as 1.49 times.
        Fact[] alsoARatio = [.. GrowthFacts, new("operating cash flow over net income", "1.49", "fundamental reading")];

        Assert.Empty(Multiples(Ungoverned, "Operating cash flow ran at 1.49 times net income [N].", alsoARatio));
    }

    [Fact]
    public void ARetryIsToldToWriteAGrowthAsARiseInPerCent()
    {
        var brief = RetryBrief.For([.. ClaimRules.Check("The segment commentary", "U.S. upstream earnings rose to $3.54 billion, 1.49 times the year-earlier figure [N].", GrowthFacts, [RegionRelease]).Findings]);

        Assert.Contains(ClaimRules.GrowthAsAMultiple, brief, StringComparison.Ordinal);
        Assert.Contains("as a rise in per cent, such as up 150%, and never as a number of times the earlier figure", brief, StringComparison.Ordinal);
    }
}
