using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, the 24.0 correction of 2026-11-20: no shipped line states or implies the price a stock will open at. A pick
// bought at the open says it buys at the next open at whatever price it opens, with its last close beside it, and a buy
// already made states the price it paid.
// see: No line states or implies the price a stock will open at, and a buy at the open buys at whatever price it opens
public sealed partial class ReadSurface
{
    // The wording that joined a price to a buy at the open, each place the correction found.
    static readonly string[] PriceAtTheOpen =
    [
        @"(?i)\bbuy at about\b",
        @"(?i)\bopen, at about\b",
        @"(?i)\bat about tonight's close\b",
        @"(?i)\bnear tonight's close\b",
        @"(?i), in at about\b",
        @"(?i)\bheld: bought by [^""]*\bat about\b",
    ];

    [Fact]
    public void NoShippedLineStatesAPriceForABuyAtTheOpen()
    {
        var separator = Path.DirectorySeparatorChar;
        var sources = new[] { "EquityBrief.Core", "EquityBrief.Worker", "EquityBrief.Api", "EquityBrief.Web" }
            .SelectMany(project => Directory.EnumerateFiles(Path.Combine(Repository.Root, "src", project), "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{separator}bin{separator}", StringComparison.Ordinal) && !path.Contains($"{separator}obj{separator}", StringComparison.Ordinal))
            .ToArray();

        Assert.True(sources.Length >= 300, $"Read {sources.Length} shipped source(s), expected at least 300.");
        Assert.Empty(sources.SelectMany(path => PriceAtTheOpen
            .Where(pattern => Regex.IsMatch(File.ReadAllText(path), pattern))
            .Select(pattern => $"{Path.GetRelativePath(Repository.Root, path)}: {pattern}")));
    }

    // The words a buy at the open says pass every pattern, and the words the correction replaced fail them.
    [Fact]
    public void TheWordsOfABuyAtTheOpenPassThePatternsAndTheWordsReplacedFailThem()
    {
        Assert.Equal("Buys at the next open, at whatever price it opens", MarkRenderer.BuysAtTheOpen);
        Assert.All(PriceAtTheOpen, pattern => Assert.DoesNotMatch(pattern, MarkRenderer.BuysAtTheOpen));
        Assert.Matches(PriceAtTheOpen[0], "Buy at about $10.00");
        Assert.Matches(PriceAtTheOpen[2], "3 stocks to buy at Monday's open, at about tonight's close.");
    }
}
