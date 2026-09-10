using System.Globalization;
using EquityBrief.Core.Shortlist;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Shortlist;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// listings-coverage. A listings row exists for every index member on every night
// the run completed.
//
// Rostered from 5.4 rather than from 5.1, because 5.4 is the checkpoint that
// creates `listing` and a roster row naming a checkpoint the record shows as
// landed fails `coverage-reported`. It was one of four rows that named a
// checkpoint which does not create the store its own reason reads.
//
// The property is section 17's row coverage limit and it is what section 13's
// shadow mechanism rests on: a shadow candidate has to be evaluated on the
// nights it would have fired, and most of those are nights no live reason
// surfaced that name. Writing rows only for listed names would make that
// impossible without anything announcing it.
// see: Candidate conditions are registered before they are scored, and scored in shadow before they are shown
//
// It reads a store the suite built rather than the live one, because a check
// that reads the live store is a check whose result depends on last night.
public class ListingsCoverage
{
    internal static CheckReach Reach => new(
        "listings-coverage",
        ["fixtures/membership-2026-09-05"],
        [
            CheckReach.Key(Scope.LimitsTable, "Nightly row coverage"),
        ]);

    [Fact]
    public async Task EveryIndexMemberHasARowOnEveryNightTheRunCompleted()
    {
        using var store = await FixtureExpectations.WithListings();

        var members = FixtureExpectation.CurrentMembers;
        var nights = Query(store, "SELECT DISTINCT session_date FROM listing ORDER BY session_date;");

        // The scope, stated in advance and floored on the thing carrying the
        // property. The nights are context; the rows per night are the claim.
        Assert.True(nights.Count >= 1, $"Read {nights.Count} night(s) of listings, expected at least 1.");
        Assert.True(members.Length >= 4, $"Read {members.Length} index member(s), expected at least 4.");

        foreach (var night in nights)
        {
            Assert.Equal(
                [.. members],
                Query(store, $"SELECT ticker FROM listing WHERE session_date = '{night}' ORDER BY ticker;"));
        }
    }

    [Fact]
    public async Task ANameThatFiredNothingStillHasARowAndSaysSo()
    {
        // The half a coverage count over the fired names would also satisfy. A
        // row with a fired count of zero is what the shadow column is written
        // against, and it is most of the table: the note in SCHEMA says zero for
        // most rows and means it.
        using var store = await FixtureExpectations.WithListings();

        var quiet = int.Parse(Query(store, "SELECT COUNT(*) FROM listing WHERE fired_count = 0;").Single(), CultureInfo.InvariantCulture);
        var loud = int.Parse(Query(store, "SELECT COUNT(*) FROM listing WHERE fired_count > 0;").Single(), CultureInfo.InvariantCulture);
        var all = int.Parse(Query(store, "SELECT COUNT(*) FROM listing;").Single(), CultureInfo.InvariantCulture);

        Assert.True(all > 0, "no listings were written at all, so the coverage above compared nothing.");

        // The two halves account for every row exactly once, which is what the
        // every-name grain means and what a count over the fired names alone
        // would not show. It is stated as a partition rather than as a
        // requirement that some row be quiet: over four names of real bars every
        // one of them fires something, and asserting otherwise would be an
        // assertion over a shape this fixture cannot take. The quiet row is
        // constructed below instead.
        Assert.Equal(all, quiet + loud);

        // Every row carries all six reasons whether or not any fired, so a
        // reason that never fires is still a reason a later session can score.
        Assert.All(
            Query(store, "SELECT reasons FROM listing;"),
            reasons => Assert.All(
                ShortlistSeries.Reasons,
                name => Assert.Contains(name, reasons, StringComparison.Ordinal)));

        // And the fired count on the row is the number of reasons that fired,
        // rather than a figure the builder stated beside them.
        foreach (var row in Query(store, "SELECT fired_count, reasons FROM listing;"))
        {
            var parts = row.Split('|', 2);
            var counted = System.Text.Json.JsonDocument.Parse(parts[1]).RootElement
                .EnumerateArray()
                .Count(reason => reason.GetProperty("fired").GetBoolean());

            Assert.Equal(int.Parse(parts[0], CultureInfo.InvariantCulture), counted);
        }

    }

    [Fact]
    public async Task AMemberTheNightComputedNothingForStillGetsARow()
    {
        // The case the committed fixture cannot reach on its own: every current
        // member of it has bars. A member with none is what the every-name grain
        // is for, and a builder that skipped it would leave a count that is
        // wrong in the direction nobody looks.
        using var store = await FixtureExpectations.WithListings();

        Insert(
            store,
            "INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at, sector) " +
            "VALUES ('GSPC', 'ZZZZ', '2026-01-02', NULL, '2026-09-08T00:00:00Z', 'Utilities');");

        await new ShortlistBuilder(
            FixedClock.At(new DateTimeOffset(2026, 9, 8, 21, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates),
            store.DatabaseFile).RunAsync("GSPC", "coverage-check");

        var rows = Query(store, "SELECT COUNT(*) FROM listing WHERE ticker = 'ZZZZ';").Single();

        Assert.Equal("1", rows);

        // Its reasons are all present and none fired, which is what a name with
        // nothing computed for it should say rather than being absent.
        var reasons = Query(store, "SELECT reasons FROM listing WHERE ticker = 'ZZZZ';").Single();

        Assert.All(ShortlistSeries.Reasons, name => Assert.Contains(name, reasons, StringComparison.Ordinal));
        Assert.Equal("0", Query(store, "SELECT fired_count FROM listing WHERE ticker = 'ZZZZ';").Single());

        // And its plan says why it is empty rather than being a blank column.
        Assert.Contains(
            "no ladder row",
            Query(store, "SELECT plan_at_listing FROM listing WHERE ticker = 'ZZZZ';").Single(),
            StringComparison.Ordinal);
    }

    static IReadOnlyList<string> Query(TemporaryStore store, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        var rows = new List<string>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount)
                .Select(field => reader.IsDBNull(field) ? "null" : reader.GetValue(field).ToString())));
        }

        return rows;
    }

    static void Insert(TemporaryStore store, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
