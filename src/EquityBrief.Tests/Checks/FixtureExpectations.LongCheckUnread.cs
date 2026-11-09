using EquityBrief.Core.Ledger;
using EquityBrief.Worker.Ledger;
using Xunit;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the 23.6 correction: the long ledger's point-in-time check leaves unread the rating actions the long
// build holds none of by design, counts them, and reads them where the long build stored one; every other reading is held
// to the rebuilt one as before.
// see: The long ledger's point-in-time check leaves unread a reading the long build holds none of by design, and says so
public partial class FixtureExpectations
{
    [Fact]
    public void TheLongCheckLeavesUnreadTheRatingActionsTheLongBuildHoldsNoneOf()
    {
        var ratings = LedgerReadings.All.Select((reading, at) => (reading, at)).Single(pair => pair.reading.Column == "rating_actions_net").at;
        var rsi = LedgerReadings.All.Select((reading, at) => (reading, at)).Single(pair => pair.reading.Column == "rsi").at;
        var stored = new double?[LedgerReadings.Count];
        var rebuilt = new double?[LedgerReadings.Count];

        rebuilt[ratings] = 3;

        // Stored none, rebuilt a count: unread on the long check, a difference on the standing one.
        var (read, unread) = LedgerPointInTime.LongDifferences(stored, rebuilt);

        Assert.Empty(read);
        Assert.Equal(1, unread);
        Assert.Equal(["rating_actions_net"], LedgerPointInTime.Differences(stored, rebuilt).Select(differ => differ.Reading));

        // Stored a count the rebuild differs from: read, and a difference.
        stored[ratings] = 2;

        var (held, none) = LedgerPointInTime.LongDifferences(stored, rebuilt);

        Assert.Equal(["rating_actions_net"], held.Select(differ => differ.Reading));
        Assert.Equal(0, none);

        // Any other reading stored none and rebuilt is still a difference on the long check.
        stored[ratings] = 3;
        rebuilt[rsi] = 40;

        Assert.Equal(["rsi"], LedgerPointInTime.LongDifferences(stored, rebuilt).Read.Select(differ => differ.Reading));
        Assert.Equal(["rating_actions_net"], LedgerPointInTime.HeldNoneByTheLongBuild);
    }
}
