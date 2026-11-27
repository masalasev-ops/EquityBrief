using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Loop;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the 24.19 correction of 2026-11-27: the per-rule test over the long history lays Quality at a fair
// price's and the Fundamentals family's books as the books' own long test lays them, each filer's long facts read beside its
// standing ones, so no book is kept with the reason that the long run lays out none; a rule the lay does not reach is still
// kept with its reason.
// see: The monthly run's long test lays the books over the long history with the long facts, as the books' own long test lays them
public partial class FixtureExpectations
{
    // The rules test's lays and the books test's, each read off the tester's own source between the long reader's facts and
    // the releases' cutoff, the one place each run chooses what it lays.
    static string LaysOf(string text, string from, string to)
    {
        var start = text.IndexOf(from, StringComparison.Ordinal);

        Assert.True(start >= 0, $"'{from}' is not in the tester's source");

        var end = text.IndexOf(to, start, StringComparison.Ordinal);

        Assert.True(end > start, $"'{to}' does not follow '{from}' in the tester's source");

        return text[start..end];
    }

    [Fact]
    public void TheLongTestLaysTheBooksOverTheLongHistoryWithTheLongFactsAsTheBooksOwnTestLaysThem()
    {
        var source = File.ReadAllText(Path.Combine(Repository.Root, "src", "EquityBrief.Worker", "Loop", "WalkForwardTester.cs"));
        var booksSource = File.ReadAllText(Path.Combine(Repository.Root, "src", "EquityBrief.Worker", "Loop", "WalkForwardTester.Books.cs"));
        var rules = LaysOf(source, "var standingOnly = longHistory is not null;", "var releaseCutoff");
        var books = LaysOf(booksSource, "var quality = ReadsQuality(standing, index)", "// What the quality lay read");

        // Both runs hand each lay the long reader's facts where a long reader stands, and the rules test lays each book
        // wherever a rule of its family stands, the long run among them.
        foreach (var lays in new[] { rules, books })
        {
            Assert.Contains("QualityLay.ReadAsync(read, history, through, output.WriteLine, cancellation, factsOf)", lays, StringComparison.Ordinal);
            Assert.Contains("ImprovingLay.ReadAsync(read, history, releases.ByFiler, through, output.WriteLine, cancellation, factsOf)", lays, StringComparison.Ordinal);
        }

        Assert.Contains("ciks => longHistory.FiledFactsAsync(ciks, through, cancellation)", rules, StringComparison.Ordinal);
        Assert.DoesNotContain("!standingOnly", rules, StringComparison.Ordinal);

        // A book the run lays no book for is still kept with its reason, which names the long run.
        Assert.Contains("lays out no book", RuleTests.BooksOnTheStanding, StringComparison.Ordinal);
    }
}
