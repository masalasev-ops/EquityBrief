using EquityBrief.Tests.Checks;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Reading;

// read-surface: an earlier night on tonight's list is drawn from what that night stored, through
// the route that serves it, over a store holding two nights whose bands disagree.
public partial class ReadSurface
{
    [Fact]
    public async Task AnEarlierNightIsOrderedByTheBandsThatNightStored()
    {
        using var store = await FixtureExpectations.WithListings();

        var (earlier, newest) = await TwoNights(store);
        var (first, last) = BandedNames(store, newest);

        // The two names fire the same reasons on both nights, so band strength is what orders
        // them. On the earlier night the name that sorts last by ticker is the stronger, and on
        // the newest night the weaker, so the right order is the reverse of the ticker order and
        // neither the newest bands nor the ticker tiebreak can produce it.
        FireAlike(store, earlier, first, last);
        FireAlike(store, newest, first, last);

        SetStrength(store, earlier, first, 1);
        SetStrength(store, earlier, last, 9);
        SetStrength(store, newest, first, 9);
        SetStrength(store, newest, last, 1);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var then = await client.GetStringAsync($"/screens/tonight/{Stamp(earlier)}");

        Assert.Contains("data-strength=\"9\"", RowOf(then, last), StringComparison.Ordinal);
        Assert.Contains("data-strength=\"1\"", RowOf(then, first), StringComparison.Ordinal);
        Assert.True(
            RowAt(then, last) < RowAt(then, first),
            $"{Stamp(earlier)} drew {first} before {last}, where that night's bands put {last} first.");

        // The counter-reading over the same store: the newest night, whose bands say the
        // opposite, draws the opposite order, so the two nights can be told apart.
        var now = await client.GetStringAsync($"/screens/tonight/{Stamp(newest)}");

        Assert.True(
            RowAt(now, first) < RowAt(now, last),
            $"{Stamp(newest)} drew {last} before {first}, where that night's bands put {first} first.");
    }

    // An earlier session the store holds bars for, listed with the newest night's listings and
    // carrying a copy of the newest night's bands as its own, so each night has a band set of
    // its own to be read from.
    static async Task<(DateOnly Earlier, DateOnly Newest)> TwoNights(TemporaryStore store)
    {
        var api = Api(store);
        var newest = (await api.NewestNightAsync())!.Value;
        var bars = await api.BarsAsync(Name, DateOnly.MinValue, DateOnly.MaxValue);
        var earlier = bars[^3].SessionDate;

        Assert.True(earlier < newest, $"the earlier session {earlier} is not before the night {newest}.");

        Insert(
            store,
            "INSERT INTO listing (ticker, session_date, reasons, fired_count, plan_at_listing, shadow_reasons) " +
            $"SELECT ticker, '{Stamp(earlier)}', reasons, fired_count, plan_at_listing, shadow_reasons " +
            $"FROM listing WHERE session_date = '{Stamp(newest)}';");

        Insert(
            store,
            "INSERT INTO level (ticker, as_of, low_edge, high_edge, role, immediate, strength, has_non_average_anchor, members) " +
            $"SELECT ticker, '{Stamp(earlier)}', low_edge, high_edge, role, immediate, strength, has_non_average_anchor, members " +
            $"FROM level WHERE as_of = '{Stamp(newest)}';");

        // Each night reads its own set, and nothing between the two holds another.
        foreach (var ticker in Strings(store, $"SELECT DISTINCT ticker FROM level WHERE as_of = '{Stamp(earlier)}';"))
        {
            Assert.All(await api.LevelsAsync(ticker, earlier), band => Assert.Equal(earlier, band.AsOf));
            Assert.All(await api.LevelsAsync(ticker, newest), band => Assert.Equal(newest, band.AsOf));
        }

        return (earlier, newest);
    }

    // The first and the last name by ticker among those holding bands on the night.
    static (string First, string Last) BandedNames(TemporaryStore store, DateOnly night)
    {
        var names = Strings(store, $"SELECT DISTINCT ticker FROM level WHERE as_of = '{Stamp(night)}';")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(names.Length >= 2, $"{names.Length} name(s) hold bands on {Stamp(night)}, expected at least 2.");

        return (names[0], names[^1]);
    }

    // Both names given the reasons of the row that fired most that night.
    static void FireAlike(TemporaryStore store, DateOnly night, string first, string last)
    {
        var on = Stamp(night);
        var fired = Strings(store, $"SELECT CAST(MAX(fired_count) AS TEXT) FROM listing WHERE session_date = '{on}';").Single();

        Assert.True(int.Parse(fired, System.Globalization.CultureInfo.InvariantCulture) >= 1, $"no name fired on {on}.");

        store.Execute(
            "UPDATE listing SET " +
            $"reasons = (SELECT reasons FROM listing WHERE session_date = '{on}' ORDER BY fired_count DESC, ticker LIMIT 1), " +
            $"fired_count = {fired} " +
            $"WHERE session_date = '{on}' AND ticker IN ('{first}', '{last}');");
    }

    static void SetStrength(TemporaryStore store, DateOnly night, string ticker, int strength) =>
        store.Execute($"UPDATE level SET strength = {strength} WHERE ticker = '{ticker}' AND as_of = '{Stamp(night)}';");

    // Where a name's row opens on the list, which is the order the list draws it in.
    static int RowAt(string list, string ticker)
    {
        var at = list.IndexOf($"<tr data-ticker=\"{ticker}\"", StringComparison.Ordinal);

        Assert.True(at >= 0, $"the list draws no row for {ticker}.");

        return at;
    }

    static IReadOnlyList<string> Strings(TemporaryStore store, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        using var reader = command.ExecuteReader();
        var values = new List<string>();

        while (reader.Read())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }
}
