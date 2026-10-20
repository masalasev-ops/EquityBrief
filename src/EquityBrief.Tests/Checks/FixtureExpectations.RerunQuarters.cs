using System.Globalization;
using EquityBrief.Data;
using EquityBrief.Worker.Indices;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the 15.1 correction of 2026-10-20: the index families read a quarter only where a night before
// theirs fetched it, as the member readings do, since the night's own quarters step runs after its index families; a
// night run again the next morning reads the quarters its first run read, worked by hand over a constructed store.
public partial class FixtureExpectations
{
    [Fact]
    public async Task ANightRunAgainReadsTheQuartersItsNightReadAndNotOnesItsOwnQuartersStepStoredAfterItsFamilies()
    {
        var night = DateOnly.ParseExact(IndexNight, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var next = new DateOnly(2026, 10, 5);

        // IA holds the four quarters every index store holds, fetched on 2026-08-01, and a fifth, a loss of 50 for the
        // quarter to 2026-09-30 filed on 2026-10-01, which the night of 2026-10-02 fetched in its quarters step after its
        // index families had read the four.
        using var store = IndexStore();
        store.Execute(
            "INSERT INTO reported_quarter (ticker, fetched_at, session_date, period_end, filing_date, net_income, operating_income) " +
            "VALUES ('IA', '2026-10-02T23:50:00Z', '2026-10-02', '2026-09-30', '2026-10-01', '-50', '-50');");
        await using var connection = new SqliteConnection(StoreConnection.For(store.DatabaseFile));
        await connection.OpenAsync();

        // That night run again reads the four its first run read, summing to 40 and passing the profit gate; the night
        // after reads all five, its newest four summing to -20.
        var (_, again) = await IndexFamilies.InputsAsync(connection, "MID", night, default);
        var (_, after) = await IndexFamilies.InputsAsync(connection, "MID", next, default);

        Assert.Equal(["2025-09-30", "2025-12-31", "2026-03-31", "2026-06-30"], again["IA"].Select(quarter => quarter.PeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        Assert.Equal(5, after["IA"].Count);
        Assert.Equal(-50m, after["IA"][^1].NetIncome);
    }
}
