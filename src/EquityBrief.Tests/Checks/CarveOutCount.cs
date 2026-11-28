using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;
using Xunit;

namespace EquityBrief.Tests.Checks;

// From the 24.47 correction of 2026-11-28: the nightly run's carve-outs are stated with no count before them, which went
// stale as each carve-out was added, and with none named by its place in the list; the reader shown to find both.
// see: The nightly run is arithmetic only
public class CarveOutCount
{
    static readonly Regex Counted = new(@"\b(?:\d+|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twenty)\s+carve-outs\b|none but the (?:first|second|third|fourth|fifth|sixth|seventh|eighth|ninth|tenth|eleventh|twelfth|\d+(?:st|nd|rd|th))\b", RegexOptions.IgnoreCase);

    public static IReadOnlyList<string> CountsIn(string text) => [.. Counted.Matches(text).Select(match => match.Value)];

    [Fact]
    public void TheNightlyRunsCarveOutsAreStatedWithNoCountAndNonePlacedByNumber()
    {
        var rules = File.ReadAllText(Path.Combine(Repository.Root, "CLAUDE.md"));

        Assert.Contains("The carve-outs named below are stated rather than left to be discovered", rules, StringComparison.Ordinal);
        Assert.Empty(CountsIn(rules));

        // The sentence as it read before the correction, found on both counts.
        Assert.Equal(["Fourteen carve-outs", "none but the tenth"], CountsIn("Fourteen carve-outs are named rather than left to be discovered, and none but the tenth asks for every member every night"));
    }
}
