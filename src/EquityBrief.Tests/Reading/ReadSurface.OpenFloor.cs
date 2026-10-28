using System.Net;
using EquityBrief.Api.Reading;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Reading;

// The drift's open floor replayed beside its live rule and registered as no rule: the walk skips a listing whose next open
// leaves under 1 of reward to its risk, spending its place on the night's list and leaving its stock free, and the drift's
// card on the S&P 500 draws the newest replay beneath its record.
public partial class ReadSurface
{
    // KEY's drift plan as its night struck it, the close 20.81 with its stop 20.04 and its target 21.92: filled at 21.00 its
    // reward to risk is 0.92 over 0.96, under 1, so the floor skips it; at 20.90 it is 1.02 over 0.86 and is bought; at or
    // under the stop it is skipped; and a plan with a trailing stop and no target is skipped by its stop alone.
    // see: The drift's open floor is replayed beside its live rule unregistered, skipping a fill whose reward to risk at the open is under 1
    [Fact]
    public void TheOpenFloorSkipsAListingWhoseNextOpenLeavesUnderOneOfRewardToItsRisk()
    {
        var key = new FamilyListing(0, 0, 0, 1, 0, 20.81, 20.04, 21.92, double.NaN, EquityBrief.Core.Families.DriftRule.CapSessions);

        Assert.True(RuleReplay.LeavesUnder(21.00, key, RuleRecorder.OpenFloor));
        Assert.False(RuleReplay.LeavesUnder(20.90, key, RuleRecorder.OpenFloor));
        Assert.True(RuleReplay.LeavesUnder(20.04, key, RuleRecorder.OpenFloor));
        Assert.False(RuleReplay.LeavesUnder(null, key, RuleRecorder.OpenFloor));

        var trailing = key with { Target = double.NaN, Trail = 2 };

        Assert.False(RuleReplay.LeavesUnder(30.00, trailing, RuleRecorder.OpenFloor));
        Assert.True(RuleReplay.LeavesUnder(20.00, trailing, RuleRecorder.OpenFloor));
    }

    // A listing the floor skips spends its place on its night's list, so the next listing on that night does not move up,
    // and holds no trade, so the same stock listed on a later night of its window is bought; every other step is the
    // family's own walk, which buys the first listing and holds its stock through its trade.
    // see: The drift's open floor is replayed beside its live rule unregistered, skipping a fill whose reward to risk at the open is under 1
    [Fact]
    public void TheWalkSkippingAtTheOpenSpendsTheSkippedPlaceAndLeavesItsStockFree()
    {
        string[] tickers = ["AAA", "BBB"];
        FamilyListing[] listings =
        [
            new(0, 10, 10, 2, 0, 100, 96, 110, double.NaN, 60),
            new(1, 10, 10, 1, 0, 50, 48, 56, double.NaN, 60),
            new(0, 11, 11, 2, 0, 101, 96, 112, double.NaN, 60),
        ];

        (double? Result, int Sessions) Exit(FamilyListing listing) => (1.0, 5);

        var skipped = 0;
        var floor = RuleReplay.WalkSkippingAtTheOpen(listings, tickers, _ => 0, Exit, _ => 0.0, listing => listing.Session == 10 && listing.Name == 0, () => skipped++, perNight: 1);
        var family = FamilySweep.Walk(listings, tickers, _ => 0, Exit, _ => 0.0, perNight: 1);

        Assert.Equal(1, skipped);
        Assert.Equal([(0, 11)], floor.Select(trade => (trade.Listing.Name, trade.Listing.Session)));
        Assert.Equal([(0, 10)], family.Select(trade => (trade.Listing.Name, trade.Listing.Session)));
        Assert.Equal(family, RuleReplay.WalkSkippingAtTheOpen(listings, tickers, _ => 0, Exit, _ => 0.0, _ => false, perNight: 1));
    }

    // The drift's card on the S&P 500 draws the newest replay beneath the rule's record, under the rule's own heading; a card
    // of another family or another index draws none. The read surface's stage is the worker's.
    // see: The drift's open floor is replayed beside its live rule unregistered, skipping a fill whose reward to risk at the open is under 1
    [Fact]
    public void TheDriftsCardOnTheSAndP500DrawsTheOpenFloorReplayBeneathItsRecord()
    {
        Assert.Equal(RuleRecorder.OpenFloorStage, ReadApi.OpenFloorStage);
        Assert.Equal(EquityBrief.Worker.Indices.IndexFamilies.LargeIndex, ReadApi.OpenFloorIndex);

        var replay = new CardOpenFloorView(1, new DateOnly(2019, 1, 2), new DateOnly(2026, 10, 27), new DateOnly(2026, 10, 27), RuleRecorder.OpenFloorWords, 410, 0.4, 0.21, 0, null, 455, 0.38, 0.17, 0, null, 52);
        var line = WebUtility.HtmlDecode(MarkRenderer.OpenFloorBeside(replay));

        Assert.Contains("Beside it, the same rule replayed skipping a fill whose reward to risk at the open is under 1, registered as no rule: over 2 Jan 2019 to 27 Oct 2026 it kept 410 trades at +0.21 of the risk a trade after costs, 40% won, where the rule's own walk kept 455 at +0.17 of the risk a trade, 38% won, and it skipped 52 listings the rule bought. Followed forward from 27 Oct 2026: 0 trades at no average yet, against the rule's 0 at no average yet.", line, StringComparison.Ordinal);
        Assert.Equal(string.Empty, MarkRenderer.OpenFloorBeside(null));

        var drift = new DecisionCardRow("GSPC", new DateOnly(2026, 10, 20), EquityBrief.Core.Families.DriftRule.Name, "KEY", 1, 20.81m, 20.04m, 21.92m, "the drift rule", "{}", "[]", null, Cap: 60);
        var context = CardContext.None with { OpenFloor = replay };

        Assert.Same(replay, CardScreen.View(drift, context).OpenFloor);
        Assert.Null(CardScreen.View(drift with { Index = "MID" }, context).OpenFloor);
        Assert.Null(CardScreen.View(drift with { Family = EquityBrief.Core.Families.BreakoutRule.Name }, context).OpenFloor);
    }
}
