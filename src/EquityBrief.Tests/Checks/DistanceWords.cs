using EquityBrief.Core.Paper;
using EquityBrief.Tests.Harness;
using Xunit;

namespace EquityBrief.Tests.Checks;

// From the operator's answers of 2026-11-28, item 5: one measure of a waiting order's distance on Home, Tonight's plans and the
// order changes, the fall from its last close to its buy price over that close, in their words.
// see: A waiting order's distance is said in one measure everywhere, the fall from its last close to its buy price
public class DistanceWords
{
    [Fact]
    public void AWaitingOrdersDistanceIsTheFallFromItsLastCloseToItsBuyPriceInTheOperatorsWords()
    {
        // ABNB's of 27 Nov, by hand: (179.16 - 171.54) / 179.16 = 4.25%, said 4.3%; over the buy price it would be 4.44%.
        Assert.Equal("Needs to fall 4.3% from its last close of $179.16 to reach its buy price of $171.54.", PlanOrderWords.ToFall(171.54m, 179.16m));
        Assert.Equal("Needs to fall 9.1% from its last close of $1,100.00 to reach its buy price of $1,000.00.", PlanOrderWords.ToFall(1000m, 1100m));

        // A close at or under the price says so, and the row's short form says no fall below nothing.
        Assert.Equal("Its last close of $99.00 is at or below its buy price of $100.00.", PlanOrderWords.ToFall(100m, 99m));
        Assert.Equal("Its last close of $100.00 is at or below its buy price of $100.00.", PlanOrderWords.ToFall(100m, 100m));
        Assert.Equal("4.3% to fall", PlanOrderWords.ToFallShort(PlanOrderWords.Gap(171.54m, 179.16m)));
        Assert.Equal("0.0% to fall", PlanOrderWords.ToFallShort(PlanOrderWords.Gap(100m, 99m)));
        Assert.Equal("no price to fall to", PlanOrderWords.ToFallShort(null));
    }

    // No shipped source draws a waiting order's distance in another measure or other words.
    [Fact]
    public void NoPageSaysAWaitingOrdersDistanceAnyOtherWay()
    {
        string[] retired = ["% above its buy price", "% to go\"", "typical daily move{plural} above", "Tonight's close was {Dollars(zone.Close)}, ", "Last close {Math.Abs(above"];
        var found = Directory.EnumerateFiles(Path.Combine(Repository.Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(Path.Combine("src", "EquityBrief.Tests"), StringComparison.Ordinal))
            .SelectMany(file => retired.Where(words => File.ReadAllText(file).Contains(words, StringComparison.Ordinal)).Select(words => Path.GetFileName(file) + ": " + words))
            .ToArray();

        Assert.Empty(found);
    }
}
