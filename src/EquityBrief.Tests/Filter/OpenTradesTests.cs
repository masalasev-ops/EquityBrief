using EquityBrief.Core.Filter;
using EquityBrief.Core.Returns;

namespace EquityBrief.Tests.Filter;

// One open trade per stock on each rule's list, the Core rule the pages read now and the filter reads from the
// freeze: a trade is open until it ends and frees the stock from the night after, a repeat never becomes the
// kept trade, each rule's trades are walked apart, and a missing outcome row reads as open until the cap passes.
// see: A stock holds one open trade on each rule's list, and it is free the night after its trade ends
public class OpenTradesTests
{
    // Sessions of the exchange's calendar in the autumn of 2026: Tuesday 2026-09-29 to Friday 2026-10-09.
    static readonly DateOnly Sep29 = new(2026, 9, 29);
    static readonly DateOnly Sep30 = new(2026, 9, 30);
    static readonly DateOnly Oct1 = new(2026, 10, 1);
    static readonly DateOnly Oct2 = new(2026, 10, 2);
    static readonly DateOnly Oct5 = new(2026, 10, 5);
    static readonly DateOnly Oct6 = new(2026, 10, 6);

    static OpenTradeListing Listed(string ticker, DateOnly night, string? outcome = null, DateOnly? resolvedOn = null, bool stored = true) =>
        new(ticker, night, stored, outcome, resolvedOn, ForwardReturnSeries.SetupSessionCap);

    // A trade that ends at a night's own close blocks that night and frees the stock from the next one,
    // whether it reached its target, fell through its stop or ran out of time.
    [Fact]
    public void ATradeEndingOnANightBlocksThatNightAndFreesTheNextOne()
    {
        foreach (var outcome in new[] { ForwardReturnSeries.Win, ForwardReturnSeries.Loss, ForwardReturnSeries.Unresolved })
        {
            Assert.True(OpenTrades.IsOpenOn(Sep29, Oct1, outcomeStored: true, outcome, resolvedOn: Oct1, ForwardReturnSeries.SetupSessionCap), outcome);
            Assert.False(OpenTrades.IsOpenOn(Sep29, Oct2, outcomeStored: true, outcome, resolvedOn: Oct1, ForwardReturnSeries.SetupSessionCap), outcome);
        }

        // An undecided trade is open on every later night, and no trade is open on its own night or before it.
        Assert.True(OpenTrades.IsOpenOn(Sep29, Oct6, outcomeStored: true, outcome: null, resolvedOn: null, ForwardReturnSeries.SetupSessionCap));
        Assert.False(OpenTrades.IsOpenOn(Sep29, Sep29, outcomeStored: true, outcome: null, resolvedOn: null, ForwardReturnSeries.SetupSessionCap));

        // A missing outcome row reads as open until the cap's sessions have passed: 63 sessions after the
        // listing it is still read as open, and on the 64th session it is not.
        var listed = new DateOnly(2026, 1, 5);
        var inside = Session(listed, ForwardReturnSeries.SetupSessionCap);
        var past = Session(listed, ForwardReturnSeries.SetupSessionCap + 1);

        Assert.True(OpenTrades.IsOpenOn(listed, inside, outcomeStored: false, outcome: null, resolvedOn: null, ForwardReturnSeries.SetupSessionCap));
        Assert.False(OpenTrades.IsOpenOn(listed, past, outcomeStored: false, outcome: null, resolvedOn: null, ForwardReturnSeries.SetupSessionCap));
    }

    // The walk names the kept trade: a listing while it is open is a repeat of it and never blocks anything
    // itself, and the first listing after it ends is the next kept trade.
    [Fact]
    public void AChainOfListingsNamesTheKeptTradeAndARepeatBlocksNothing()
    {
        var walked = OpenTrades.Walk(
        [
            // Listed on the 29th, open through the 1st, ended at the close of the 2nd.
            Listed("AG", Sep29, ForwardReturnSeries.Loss, Oct2),
            // Listed again on the 30th and the 2nd: both repeats of the 29th, the 2nd being the night it ended.
            Listed("AG", Sep30),
            Listed("AG", Oct2),
            // The 5th is free, so it is the kept trade, and the 6th repeats the 5th and not the 29th.
            Listed("AG", Oct5),
            Listed("AG", Oct6),
        ]);

        Assert.Null(walked[("AG", Sep29)]);
        Assert.Equal(Sep29, walked[("AG", Sep30)]);
        Assert.Equal(Sep29, walked[("AG", Oct2)]);
        Assert.Null(walked[("AG", Oct5)]);
        Assert.Equal(Oct5, walked[("AG", Oct6)]);

        // The kept trade open on a night, among the listings before it: the 29th's on the 1st, the 5th's on
        // the 6th, and none on the 5th, the 29th's having ended at the close of the 2nd.
        var listings = new[] { Listed("AG", Sep29, ForwardReturnSeries.Loss, Oct2), Listed("AG", Sep30), Listed("AG", Oct5) };

        Assert.Equal(Sep29, OpenTrades.OpenOn("AG", Oct1, listings)?.Night);
        Assert.Null(OpenTrades.OpenOn("AG", Oct5, listings));
        Assert.Equal(Oct5, OpenTrades.OpenOn("AG", Oct6, listings)?.Night);
    }

    // Each rule's trades are walked apart, so one rule's open trade never blocks another's listing of the
    // same stock, and stocks never block each other.
    [Fact]
    public void EachRulesTradesAreWalkedApartAndStocksNeverBlockEachOther()
    {
        var live = new[] { Listed("AG", Sep29), Listed("AG", Oct1), Listed("BS", Oct1) };
        var candidate = new[] { Listed("AG", Oct1), Listed("AG", Oct2) };

        var liveWalk = OpenTrades.Walk(live);
        var candidateWalk = OpenTrades.Walk(candidate);

        // The live list's second listing of AG repeats its first; the candidate's first listing of AG on the
        // same night is its own kept trade, the live trade blocking nothing on the candidate's list.
        Assert.Equal(Sep29, liveWalk[("AG", Oct1)]);
        Assert.Null(candidateWalk[("AG", Oct1)]);
        Assert.Equal(Oct1, candidateWalk[("AG", Oct2)]);
        Assert.Null(liveWalk[("BS", Oct1)]);

        // The exclusion the filter writes from the freeze names the open trade's night, and is known as one.
        Assert.Equal("an open trade from 2026-09-29", OpenTrades.Exclusion(Sep29));
        Assert.True(OpenTrades.IsExclusion(OpenTrades.Exclusion(Sep29)));
        Assert.False(OpenTrades.IsExclusion("an earnings date inside the holding window"));
    }

    // The session a number of sessions after a day on the exchange's calendar.
    static DateOnly Session(DateOnly from, int sessions)
    {
        var day = from;

        for (var counted = 0; counted < sessions;)
        {
            day = day.AddDays(1);

            if (day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday && EquityBrief.Core.Bars.ExchangeClosures.IsSession(day))
            {
                counted++;
            }
        }

        return day;
    }
}
