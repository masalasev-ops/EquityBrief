using EquityBrief.Core.Cards;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 16.3: the market events a pick's card reads inside a hold, from the table committed at the root
// and carried in the build, each naming the page it was read from, and a hold running past a kind's last date saying so.
// see: Market events inside a hold are read from a committed table of the Fed's and the BLS's own dates, and asked of no provider
public partial class FixtureExpectations
{
    [Fact]
    public void TheEventsTableIsTheOneCommittedAtTheRootAndEachRowNamesItsPage()
    {
        // The build carries the root's table byte for byte: sixteen FOMC decisions over 2026 and 2027 and the twelve CPI
        // releases of 2026, the BLS having published none of 2027's.
        using (var committed = File.OpenRead(Path.Combine(Repository.Root, "market-events.json")))
        {
            Assert.Equal(MarketEvents.Read(committed), MarketEvents.All);
        }

        Assert.Equal((16, 12), (MarketEvents.All.Count(one => one.Kind == "fomc"), MarketEvents.All.Count(one => one.Kind == "cpi")));
        Assert.All(MarketEvents.All, one => Assert.StartsWith("https://www.", one.Source, StringComparison.Ordinal));
        Assert.Equal(new DateOnly(2027, 12, 8), MarketEvents.All.Where(one => one.Kind == "fomc").Max(one => one.Date));
        Assert.Equal(new DateOnly(2026, 12, 10), MarketEvents.All.Where(one => one.Kind == "cpi").Max(one => one.Date));
    }

    [Fact]
    public void AHoldReadsTheEventsInsideItAndSaysWhereItRunsPastTheTable()
    {
        // A hold from 2026-10-07 through 2026-11-04 carries the CPI release of 10-14, the FOMC decision of 10-28 and the
        // CPI release of 11-10 not; it ends before both kinds' tables do.
        var (inside, past) = MarketEvents.Within(new DateOnly(2026, 10, 7), new DateOnly(2026, 11, 4));

        Assert.Equal(["2026-10-14 CPI release", "2026-10-28 FOMC decision"], inside.Select(one => FormattableString.Invariant($"{one.Date:yyyy-MM-dd} {one.Name}")));
        Assert.Empty(past);

        // A hold through 2027-01-05 runs past the CPI table's 2026-12-10 and says so, and not past the FOMC table's.
        Assert.Equal(["CPI release"], MarketEvents.Within(new DateOnly(2026, 12, 1), new DateOnly(2027, 1, 5)).PastTheTable);

        // An event on the hold's first and last session is inside it, and one a day either side is not.
        Assert.Single(MarketEvents.Within(new DateOnly(2026, 10, 14), new DateOnly(2026, 10, 14)).Inside);
        Assert.Empty(MarketEvents.Within(new DateOnly(2026, 10, 15), new DateOnly(2026, 10, 27)).Inside);
    }
}
