using EquityBrief.Core.Providers;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker;

namespace EquityBrief.Tests.Providers;

// Where tonight's feeds come from, and the two directions that must not fall
// back.
//
// Until 2.6 the choice lived in whichever overload the caller happened to call,
// so a scheduled night's source was a property of a shell script. What replaces
// it is a setting, and what makes the setting safe is that neither direction
// recovers from the other: a fixture folder that does not exist must not resolve
// to the provider, and a live source with no key must not resolve to a capture.
//
// Both are failures that look like successes. A mistyped path would spend the
// allowance and store live bars where a replay was meant; a quiet fall-back to
// yesterday's capture would look exactly like a night that ran.
public class NightSourceTests
{
    const string Key = "demo-key-not-a-real-one";

    static string Folder() => Path.Combine(Repository.Root, "fixtures", "membership-2026-09-05");

    [Fact]
    public void AFixtureSourceResolvesToFeedsThatCannotReachTheNetwork()
    {
        var feeds = NightFeeds.Resolve(NightFeeds.FixtureSource, Folder(), null, null);

        // Asked of the objects rather than of the setting that produced them.
        // The recorded feeds hold no client, so a night over a capture cannot
        // make a request whatever else goes wrong.
        Assert.False(feeds.ReachesTheNetwork);
        Assert.Equal(0, feeds.Requests);
        Assert.Equal(0, feeds.WeightedCalls);

        // And no key was needed to get here, which is the reason the source is
        // resolved before anything asks for one: CI has no business holding one.
        Assert.NotNull(feeds.Bulk);
    }

    [Fact]
    public void ALiveSourceResolvesToFeedsThatCan()
    {
        var feeds = NightFeeds.Resolve(NightFeeds.LiveSource, null, null, Key);

        Assert.True(feeds.ReachesTheNetwork);

        // Every one of the six, and not merely the first. A feed added live and
        // left out of the check would read as one that cannot, which is what
        // happened: this list said five and the record held six from 4.3 until
        // 6.1, and the calendar was the one missing from both this and the reader.
        Assert.IsType<EodhdIndexMembershipFeed>(feeds.Membership);
        Assert.IsType<EodhdHistoricalBarFeed>(feeds.Historical);
        Assert.IsType<EodhdBulkPriceFeed>(feeds.Bulk);
        Assert.IsType<EodhdCorporateActionFeed>(feeds.Corporate);
        Assert.IsType<EodhdEarningsCalendarFeed>(feeds.Calendar);
        Assert.IsType<EodhdNewsFeed>(feeds.News);
    }

    [Fact]
    public void OneLiveFeedAmongCapturesIsASetThatReachesTheNetwork()
    {
        // The permanent proof that the reader can fail, and the test the omission
        // above needed. Nothing configurable builds a mixed set, so the
        // all-or-nothing pair either side of this cannot tell a reader that names
        // five of six from one that names all six: both answer correctly when
        // every feed is the same kind. Constructed rather than resolved, one
        // member at a time, so a seventh member left out of the reader fails here.
        var captures = NightFeeds.FromFixture(Folder());

        Assert.False(captures.ReachesTheNetwork);

        var live = NightFeeds.Live(null, Key);

        NightFeeds[] mixed =
        [
            captures with { Membership = live.Membership },
            captures with { Historical = live.Historical },
            captures with { Bulk = live.Bulk },
            captures with { Corporate = live.Corporate },
            captures with { Calendar = live.Calendar },
            captures with { News = live.News },
        ];

        Assert.All(mixed, feeds => Assert.True(feeds.ReachesTheNetwork));
    }

    [Fact]
    public void AFixturePathThatDoesNotExistIsRefusedAndNeverReachesTheNetwork()
    {
        var missing = Path.Combine(Repository.Root, "fixtures", "no-such-fixture");

        var refusal = Assert.Throws<DirectoryNotFoundException>(
            () => NightFeeds.Resolve(NightFeeds.FixtureSource, missing, null, Key));

        // The key is present and correct, so a fall-back would have succeeded.
        // That is what makes this the direction worth asserting: the failure it
        // guards against is a night that worked and did the wrong thing.
        Assert.Contains("no such folder exists", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("resolved to the provider", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFixtureSourceWithNoFolderIsRefusedRatherThanRunLive()
    {
        var refusal = Assert.Throws<InvalidOperationException>(
            () => NightFeeds.Resolve(NightFeeds.FixtureSource, null, null, Key));

        Assert.Contains(NightFeeds.FixtureKey, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ALiveSourceWithNoKeyIsRefusedRatherThanFallBackToACapture()
    {
        // The other direction. A capture is present and readable, so a
        // fall-back would have produced a night that ran and stored yesterday.
        foreach (var blank in new string?[] { null, "", " " })
        {
            var refusal = Assert.Throws<InvalidOperationException>(
                () => NightFeeds.Resolve(NightFeeds.LiveSource, Folder(), null, blank));

            Assert.Contains(ProviderCredentials.ApiKeyName, refusal.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ASourceThatIsNeitherIsRefusedRatherThanGuessedAt()
    {
        foreach (var wrong in new[] { "provider", "recorded", "LIVE ", "fixtures" })
        {
            if (string.Equals(wrong.Trim(), NightFeeds.LiveSource, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var refusal = Assert.Throws<InvalidOperationException>(
                () => NightFeeds.Resolve(wrong, Folder(), null, Key));

            Assert.Contains(NightFeeds.SourceKey, refusal.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheDefaultIsTheProviderAndTheSpellingIsForgivingOnCaseAlone()
    {
        // Absent means live, because the provider is what a scheduled night is
        // for and a night that quietly replayed a capture is the failure this
        // whole checkpoint is about.
        foreach (var absent in new string?[] { null, "", "   " })
        {
            Assert.True(NightFeeds.Resolve(absent, null, null, Key).ReachesTheNetwork);
        }

        Assert.True(NightFeeds.Resolve("LIVE", null, null, Key).ReachesTheNetwork);
        Assert.False(NightFeeds.Resolve("Fixture", Folder(), null, Key).ReachesTheNetwork);

        // Trimmed, because a setting read out of a file carries whatever
        // whitespace the file had.
        Assert.False(NightFeeds.Resolve("  fixture  ", Folder(), null, Key).ReachesTheNetwork);
    }

    [Fact]
    public void TheNightlyScriptTakesNoFixtureArgumentOfItsOwn()
    {
        // The claim this checkpoint makes about a surface. The script's own text
        // said the fixture argument was there because the live feeds did not
        // exist and would become optional when they did, and this is what says
        // it happened.
        var script = File.ReadAllText(Repository.Tool("nightly"));

        Assert.Contains("nightly \"$@\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("--fixture \"$fixture\"", script, StringComparison.Ordinal);
        Assert.Contains(NightFeeds.SourceKey, script, StringComparison.Ordinal);
    }
}
