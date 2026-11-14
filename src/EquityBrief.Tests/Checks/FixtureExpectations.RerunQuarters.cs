using System.Globalization;
using EquityBrief.Core.Readings;
using EquityBrief.Data;
using EquityBrief.Worker.Indices;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the 15.1 correction of 2026-10-20, inverted from 23.10's second part: the night's quarters fetch runs
// before its index families, so the families read the quarter the night stored, and a night run again the next morning
// reads what its night read; the night before reads none of it. Each quarter is read from its filing, worked by hand over
// a constructed store.
// see: The rules read each quarter from its filing and then its release from the moment the SEC made it public, and the cover from the four newest quarters stating interest
public partial class FixtureExpectations
{
    [Fact]
    public async Task TheIndexFamiliesReadTheQuarterTheNightsOwnFetchStoredAndANightRunAgainReadsTheSame()
    {
        var night = DateOnly.ParseExact(IndexNight, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var before = new DateOnly(2026, 10, 1);

        // IA holds the four quarters every index store holds, fetched on 2026-08-01, and a fifth, a loss of 50 for the
        // quarter to 2026-09-30 filed on 2026-10-01, which the night of 2026-10-02 fetched in its quarters step before its
        // index families read, its filing stating the loss.
        using var store = IndexStore();
        store.Execute(
            "INSERT INTO reported_quarter (ticker, fetched_at, session_date, period_end, filing_date, net_income, operating_income) " +
            "VALUES ('IA', '2026-10-02T23:50:00Z', '2026-10-02', '2026-09-30', '2026-10-01', '-50', '-50');");
        QuarterFacts(store, FilerOf(store, "IA"), "2026-09-30", "2026-10-01", ("NetIncomeLoss", -50m), ("OperatingIncomeLoss", -50m));

        await using var connection = new SqliteConnection(StoreConnection.For(store.DatabaseFile));
        await connection.OpenAsync();

        // The night, and so the night run again, reads all five, its newest four summing to -20 and failing the profit gate;
        // the night before reads the four its own fetch held, summing to 40 and passing.
        var (_, tonight) = await IndexFamilies.InputsAsync(connection, "MID", night, default);
        var (_, earlier) = await IndexFamilies.InputsAsync(connection, "MID", before, default);

        static string[] Ends(IReadOnlyList<FiledIncome> quarters) =>
            [.. quarters.Select(quarter => quarter.PeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Order(StringComparer.Ordinal)];

        Assert.Equal(["2025-09-30", "2025-12-31", "2026-03-31", "2026-06-30", "2026-09-30"], Ends(tonight["IA"]));
        Assert.Equal(-50m, tonight["IA"].Single(quarter => quarter.PeriodEnd == new DateOnly(2026, 9, 30)).NetIncome);
        Assert.False(MemberReadings.Profit(tonight["IA"], night));

        Assert.Equal(["2025-09-30", "2025-12-31", "2026-03-31", "2026-06-30"], Ends(earlier["IA"]));
        Assert.True(MemberReadings.Profit(earlier["IA"], before));
    }
}
