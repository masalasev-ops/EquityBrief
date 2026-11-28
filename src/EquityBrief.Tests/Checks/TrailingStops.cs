using EquityBrief.Core.Cards;
using EquityBrief.Core.Paper;
using EquityBrief.Tests.Harness;
using Xunit;

namespace EquityBrief.Tests.Checks;

// From the operator's answers of 2026-11-28, item 4: each night's raised stop of a trailing trade is stored, and a sale above
// its first stop reads as sold on its trailing stop, never its time limit, in the order changes and the closed-trade lists.
// see: A trailing trade's stop is stored as each night raises it, and a sale above its first stop is sold on its trailing stop
public class TrailingStops
{
    static readonly DateOnly Listed = new(2026, 11, 2);

    static readonly DateOnly[] Sessions = [new(2026, 11, 2), new(2026, 11, 3), new(2026, 11, 4), new(2026, 11, 5), new(2026, 11, 6)];

    // Bought at 100 on its listing night with its first stop at 95 and a trail of 5; its closes 100, 104, 108, 103 and 101.
    static readonly decimal[] Closes = [100m, 104m, 108m, 103m, 101m];

    static PaperTrade Trade(DateOnly? ended = null, decimal? sale = null) =>
        new("TRL", Listed, 100m, 95m, ended, sale, null, 1, Listed: Listed, Trail: 5m, Cap: 5);

    static decimal? Close(string ticker, DateOnly session) =>
        Array.IndexOf(Sessions, session) is var at and >= 0 ? Closes[at] : null;

    [Fact]
    public void ATrailingTradesStopRisesUnderEachCloseAndTheNightBeforeASaleIsTheOneReadAgainstIt()
    {
        // By hand: 95, then 104 - 5 = 99, 108 - 5 = 103, and 103 - 5 = 98 and 101 - 5 = 96 lower nothing.
        Assert.Equal(99m, PaperAccount.RaisedStop(Trade(), Sessions[1], Sessions, Close));
        Assert.Equal(103m, PaperAccount.RaisedStop(Trade(), Sessions[4], Sessions, Close));

        // A trade open on the night gets its row; one ended by then, and one with no trail, get none.
        Assert.Equal([new TrailStopRow("breakout", "TRL", Listed, Sessions[4], 95m, 103m)], TrailStops.Of("breakout", [Trade(), Trade(Sessions[3], 102m), Trade() with { Trail = null, Ticker = "FIX" }], Sessions[4], Sessions, Close));

        TrailStopRow[] stored = [new("breakout", "TRL", Listed, Sessions[1], 95m, 99m), new("breakout", "TRL", Listed, Sessions[2], 95m, 103m), new("pullback", "TRL", Listed, Sessions[3], 95m, 120m)];

        Assert.Equal(103m, TrailStops.Before(stored, "breakout", "TRL", Listed, Sessions[4]));
        Assert.Equal(99m, TrailStops.Before(stored, "breakout", "TRL", Listed, Sessions[2]));
        Assert.Null(TrailStops.Before(stored, "breakout", "TRL", Listed, Sessions[1]));
        Assert.Null(TrailStops.Before(null, "breakout", "TRL", Listed, Sessions[4]));
    }

    [Fact]
    public void ASaleAboveTheFirstStopIsSoldOnItsTrailingStopNeverItsTimeLimit()
    {
        // Held 14 sessions against a limit of 5: before this a sale at 102 above its first stop read its time limit.
        var sold = Trade(new DateOnly(2026, 11, 20), 102m);
        var ended = PaperAccount.Ended(sold, 0.4, raised: 103m);

        Assert.Equal(PaperAccount.UnderTrail, ended.Ending);
        Assert.Equal("closed under its trailing stop of $103.00", ended.Said);
        Assert.Equal(PaperAccount.UnderTrail, PaperAccount.Ended(sold, 0.4).Ending);
        Assert.Equal(PaperAccount.UnderTrail, PaperAccount.Ended(sold, 0.4).Said);

        // Sold under its first stop it was stopped, unless a night stored its stop above the first, its open having gapped down.
        Assert.Equal(PaperAccount.UnderStop, PaperAccount.Ended(sold with { Sale = 94m }, -1.2).Ending);
        Assert.Equal(PaperAccount.UnderTrail, PaperAccount.Ended(sold with { Sale = 94m }, -1.2, raised: 103m).Ending);

        // A close fired above its first stop past its limit fired on its trailing stop, as the trade waits for its sale.
        Assert.Equal(PaperAccount.UnderTrail, PaperAccount.WhyFired(101m, null, 95m, 5m, 14, 5));
        Assert.Equal(PaperAccount.TimeRanOut, PaperAccount.WhyFired(101m, null, 95m, null, 14, 5));
    }

    [Fact]
    public void TheOrderChangesSayASaleOnItsTrailingStopWithTheStopStoredTheNightBeforeItFired()
    {
        var night = new DateOnly(2026, 11, 25);
        var fired = new DateOnly(2026, 11, 24);
        var sold = Trade(night, 102m) with { FiredOn = fired };
        TrailStopRow[] stored = [new("breakout", "TRL", Listed, new DateOnly(2026, 11, 23), 95m, 103m)];

        IndexOrders Index(IReadOnlyList<TrailStopRow>? raised) =>
            new("GSPC", "S&P 500", [], [new AccountOrders("New high on the S&P 500", false, [sold], [], "breakout")], [], new Dictionary<string, decimal>(StringComparer.Ordinal), raised);

        Assert.Equal(["TRL, New high on the S&P 500: sold at $102.00 on its trailing stop of $103.00, +2.0%"], OrderChanges.Of(night, fired, [Index(stored)]).Of(OrderChanges.Sold).Select(change => change.Words));
        Assert.Equal(["TRL, New high on the S&P 500: sold at $102.00 on its trailing stop, +2.0%"], OrderChanges.Of(night, fired, [Index(null)]).Of(OrderChanges.Sold).Select(change => change.Words));
    }

    [Fact]
    public void TheStoredStopsTakeInsertsAlone()
    {
        using var store = new TemporaryStore().Migrated();

        store.Execute("INSERT INTO trail_stop (run_id, session_date, index_code, family, ticker, listed_on, first_stop, stop, written_at) VALUES ('order-changes-1', '2026-11-25', 'GSPC', 'breakout', 'TRL', '2026-11-02', '95', '103', '2026-11-26T00:00:00Z');");

        Assert.ThrowsAny<Exception>(() => store.Execute("UPDATE trail_stop SET stop = '104';"));
        Assert.ThrowsAny<Exception>(() => store.Execute("DELETE FROM trail_stop;"));
    }
}
