using EquityBrief.Core.Facts;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Sweep;
using EquityBrief.Data;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Ledger;
using EquityBrief.Worker.Sweep;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the January pin move: the night reads a stock's short interest from the store's reports through the
// one function every build reads it through, and a stock FINRA left out of the two newest settlements published before the
// session reads none rather than its old count.
// see: A build reads a stock's short interest only from the two newest settlements FINRA published before the session
public partial class FixtureExpectations
{
    [Fact]
    public async Task OnTheNightsOwnPathAStockLeftOutOfTheTwoNewestSettlementsReadsNoShortInterest()
    {
        using var store = new TemporaryStore().Migrated();

        // Three settlements, published on 9 and 26 October and 9 November. AAA was reported in the first alone; BBB in all three.
        store.Execute(
            "INSERT INTO short_interest (ticker, settlement_date, published_on, shares_short, observed_at, run_id) VALUES "
            + "('AAA', '2026-09-30', '2026-10-09', 1000000, '2026-11-10T12:00:00Z', 'short-interest-x'), "
            + "('BBB', '2026-09-30', '2026-10-09', 2000000, '2026-11-10T12:00:00Z', 'short-interest-x'), "
            + "('BBB', '2026-10-15', '2026-10-26', 2500000, '2026-11-10T12:00:00Z', 'short-interest-x'), "
            + "('BBB', '2026-10-31', '2026-11-09', 3000000, '2026-11-10T12:00:00Z', 'short-interest-x');");

        await using var connection = new SqliteConnection(StoreConnection.For(store.DatabaseFile));
        await connection.OpenAsync();

        // The night's own reader of the store's reports, as its ledger step hands them to the catalogue's context.
        var shortInterest = await SetupLedger.ShortInterestAsync(connection, ["AAA", "BBB"], CancellationToken.None);
        FiledFactRow Covers(string ticker) => new(FiledFacts.SharesOutstanding[0], new DateOnly(2026, 6, 30), new DateOnly(2026, 6, 30), 10_000_000m, new DateOnly(2026, 8, 1), "10-Q", "q-" + ticker);
        var context = new LedgerContext(NoIncome, NoSectors, [], [], new Dictionary<string, IReadOnlyList<FiledFactRow>>(StringComparer.Ordinal)
        {
            ["AAA"] = [Covers("AAA")],
            ["BBB"] = [Covers("BBB")],
        })
        {
            ShortInterest = shortInterest,
        };

        var sessions = new[] { new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 27), new DateOnly(2026, 11, 10) };
        var sessionAt = sessions.Select((day, at) => (day, at)).ToDictionary(pair => pair.day, pair => pair.at);
        SweepSeries Series(string ticker) => SweepColumns.Series(new SweepName(ticker, [.. sessions.Select(day => new SweepBar(day, 10m, 10m, 10m, 1000, 10m, 10m))], [(null, null)], []), sessionAt);
        double?[] Read(string ticker) => [.. sessions.Select(day => context.ShortInterestShare(Series(ticker), day) is { } share ? Math.Round(share, 12) : (double?)null)];

        // By hand over the covers' ten million: AAA a tenth while its settlement is of the two newest, on 12 and 27 October,
        // and none on 10 November, when the newest two were published on 26 October and 9 November; BBB a fifth, a quarter
        // and three tenths, its newest report each time.
        Assert.Equal([0.1, 0.1, null], Read("AAA"));
        Assert.Equal([0.2, 0.25, 0.3], Read("BBB"));
        Assert.Equal(2, LedgerContext.ShortInterestSettlementsRead);
    }
}
