using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Worker.Research;

namespace EquityBrief.Tests.Checks;

// The fixture's research pass asked of Claude Sonnet 5.5 under the fixture's claude-sonnet profile, over
// the answers Claude gave to the instructions every research model is asked under. Its pass on MDT left
// the short version and the cause of each large move out, refused twice for sentences naming no document:
// a short version's framing sentences, a risk's own sentence ahead of the confirmation it cited, and an
// answer written as an invisible character where there was nothing to write, then sentences saying so.
// Asked under the instructions that name those sentences, no sentence Claude wrote over the fixture names
// no document, and where it found nothing to write it answered with no text at all.
public partial class FixtureExpectations
{
    [Fact]
    public async Task ThePassAskedOfClaudeWritesNoSentenceNamingNoDocumentAndAnswersNothingWithNoText()
    {
        var paid = new RecordedResearchModelFeed(Folder(), Providers.ResearchModelFeedTests.PinnedClaude());

        using var store = await FixtureReplay.ResearchedAsync(paid: paid);

        // Every paid call was Claude's, and each section it wrote is on the store under its model.
        Assert.NotEmpty(paid.Asked);
        Assert.All(paid.Asked, request => Assert.Equal(paid.Identity, request.Model));

        var written = Query(store, $"SELECT section || '|' || status || '|' || COALESCE(reject_reason, '') FROM research_section WHERE model = '{paid.Identity}' ORDER BY section, version;");

        Assert.NotEmpty(written);
        Assert.All(written, row => Assert.DoesNotContain(ClaimRules.Uncited, row, StringComparison.Ordinal));

        // The two cases, the risks and the short version, which the comparison found refused or general, each
        // accepted.
        foreach (var section in new[] { "The two cases", "The risks, each with what would confirm it", "The short version" })
        {
            Assert.Contains(written, row => row.StartsWith(section + "|" + ClaimChecker.Accepted + "|", StringComparison.Ordinal));
        }

        // The cause, which Claude found nothing in the documents beside each move to write from: answered with
        // a thinking block and no text, asked for once more, and left out saying so, with no draft stored.
        Assert.DoesNotContain(written, row => row.StartsWith(ClaimRules.CauseSection + "|", StringComparison.Ordinal));
        Assert.Equal(
            [SpendCap.Refused, SpendCap.Refused],
            Query(store, $"SELECT outcome FROM run_log WHERE stage LIKE 'research call: {ClaimRules.CauseSection}%' ORDER BY rowid;"));
        Assert.All(
            Query(store, $"SELECT detail FROM run_log WHERE stage LIKE 'research call: {ClaimRules.CauseSection}%';"),
            detail => Assert.Contains("carried no text: it held 1 thinking block", detail, StringComparison.Ordinal));
    }
}
