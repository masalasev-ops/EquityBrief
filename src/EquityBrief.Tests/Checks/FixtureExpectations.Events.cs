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
        // The build carries the root's table byte for byte: seventeen FOMC decisions over 2026, 2027 and January 2028 and
        // the twenty-four CPI releases of 2026 and 2027.
        using (var committed = File.OpenRead(Path.Combine(Repository.Root, "market-events.json")))
        {
            Assert.Equal(MarketEvents.Read(committed), MarketEvents.All);
        }

        Assert.Equal((17, 24), (MarketEvents.All.Count(one => one.Kind == "fomc"), MarketEvents.All.Count(one => one.Kind == "cpi")));
        Assert.All(MarketEvents.All, one => Assert.StartsWith("https://www.", one.Source, StringComparison.Ordinal));
        Assert.Equal(new DateOnly(2028, 1, 26), MarketEvents.All.Where(one => one.Kind == "fomc").Max(one => one.Date));
        Assert.Equal(new DateOnly(2027, 12, 10), MarketEvents.All.Where(one => one.Kind == "cpi").Max(one => one.Date));

        // 2027's releases as the BLS's schedule page listed them on 2026-10-10, and the Fed's two-day meeting of January
        // 25 and 26, 2028, its decision on the second day. Each row states the day it was read: those thirteen on the
        // 10th, and every row the table held before them on 2026-10-06.
        string[] cpiOf2027 = ["2027-01-13", "2027-02-11", "2027-03-10", "2027-04-13", "2027-05-12", "2027-06-10", "2027-07-14", "2027-08-11", "2027-09-14", "2027-10-14", "2027-11-10", "2027-12-10"];

        Assert.Equal(cpiOf2027, MarketEvents.All.Where(one => one.Kind == "cpi" && one.Date.Year == 2027).Select(one => FormattableString.Invariant($"{one.Date:yyyy-MM-dd}")));
        Assert.Contains(MarketEvents.All, one => one.Kind == "fomc" && one.Date == new DateOnly(2028, 1, 26));

        using var table = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(Repository.Root, "market-events.json")));
        var rows = table.RootElement.GetProperty("events").EnumerateArray()
            .Select(row => (Row: row.GetProperty("kind").GetString() + " " + row.GetProperty("date").GetString(), Read: row.GetProperty("read").GetString()))
            .ToArray();

        Assert.Equal(
            [.. cpiOf2027.Select(date => "cpi " + date), "fomc 2028-01-26"],
            rows.Where(row => row.Read == "2026-10-10").Select(row => row.Row).Order(StringComparer.Ordinal));
        Assert.All(rows.Where(row => row.Read != "2026-10-10"), row => Assert.Equal("2026-10-06", row.Read));
    }

    [Fact]
    public void AHoldReadsTheEventsInsideItAndSaysWhereItRunsPastTheTable()
    {
        // A hold from 2026-10-07 through 2026-11-04 carries the CPI release of 10-14, the FOMC decision of 10-28 and the
        // CPI release of 11-10 not; it ends before both kinds' tables do.
        var (inside, past) = MarketEvents.Within(new DateOnly(2026, 10, 7), new DateOnly(2026, 11, 4));

        Assert.Equal(["2026-10-14 CPI release", "2026-10-28 FOMC decision"], inside.Select(one => FormattableString.Invariant($"{one.Date:yyyy-MM-dd} {one.Name}")));
        Assert.Empty(past);

        // A hold through 2027-01-05 runs past neither table now 2027's releases are in it; one through 2028-01-05 runs past
        // the CPI table's 2027-12-10 and says so, and not past the FOMC table's 2028-01-26.
        Assert.Empty(MarketEvents.Within(new DateOnly(2026, 12, 1), new DateOnly(2027, 1, 5)).PastTheTable);
        Assert.Equal(["CPI release"], MarketEvents.Within(new DateOnly(2027, 12, 1), new DateOnly(2028, 1, 5)).PastTheTable);

        // An event on the hold's first and last session is inside it, and one a day either side is not.
        Assert.Single(MarketEvents.Within(new DateOnly(2026, 10, 14), new DateOnly(2026, 10, 14)).Inside);
        Assert.Empty(MarketEvents.Within(new DateOnly(2026, 10, 15), new DateOnly(2026, 10, 27)).Inside);
    }
}
