using EquityBrief.Tests.Checks;
using EquityBrief.Web.App;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Reading;

// Tonight's page and the run page each carry a calendar over the nights the store holds, with the
// stored night before and after the one drawn beside it, so an earlier night is a click away rather
// than a route typed by hand.
public partial class ReadSurface
{
    // Worked by hand over four stored nights, a Thursday and a Friday and the Monday and Tuesday after
    // them. The Monday's night before is the Friday, across the weekend no night was stored for, and its
    // night after is the Tuesday; the calendar runs from the first night to the last and lists all four.
    // The first night has none before it, and the last none after it and no link to the newest.
    [Fact]
    public void ADatedScreensCalendarOffersTheStoredNightsAndTheNightsEitherSide()
    {
        DateOnly[] held = [new(2026, 9, 17), new(2026, 9, 18), new(2026, 9, 21), new(2026, 9, 22)];

        var monday = Cards.NightPicker(held[2], held, SinglePageApp.NightRoute, "#/");

        Assert.Contains("<span class=\"night-picker\" data-route=\"#/night/\" data-night=\"2026-09-21\">", monday, StringComparison.Ordinal);
        Assert.Contains("data-move=\"earlier\" href=\"#/night/2026-09-18\"", monday, StringComparison.Ordinal);
        Assert.Contains("data-move=\"later\" href=\"#/night/2026-09-22\"", monday, StringComparison.Ordinal);
        Assert.Contains(
            "value=\"2026-09-21\" min=\"2026-09-17\" max=\"2026-09-22\" data-nights=\"2026-09-17 2026-09-18 2026-09-21 2026-09-22\"",
            monday,
            StringComparison.Ordinal);
        Assert.Contains("<a class=\"np-newest\" href=\"#/\">", monday, StringComparison.Ordinal);

        // Handed in any order, the nights read the same.
        Assert.Equal(monday, Cards.NightPicker(held[2], [.. held.Reverse()], SinglePageApp.NightRoute, "#/"));

        var first = Cards.NightPicker(held[0], held, SinglePageApp.RunRoute, SinglePageApp.RunRoute);

        Assert.Contains("<span class=\"np-move\" data-move=\"earlier\" aria-disabled=\"true\">", first, StringComparison.Ordinal);
        Assert.Contains("data-move=\"later\" href=\"#/run/2026-09-18\"", first, StringComparison.Ordinal);

        var last = Cards.NightPicker(held[3], held, SinglePageApp.NightRoute, "#/");

        Assert.Contains("data-move=\"earlier\" href=\"#/night/2026-09-21\"", last, StringComparison.Ordinal);
        Assert.Contains("<span class=\"np-move\" data-move=\"later\" aria-disabled=\"true\">", last, StringComparison.Ordinal);
        Assert.DoesNotContain("np-newest", last, StringComparison.Ordinal);

        // A store holding no night draws no calendar.
        Assert.Empty(Cards.NightPicker(held[0], [], SinglePageApp.NightRoute, "#/"));
    }

    // Served over a store holding two nights, both dated screens draw the calendar over the nights the
    // listings hold, read off the store itself, each on its own route, and the page's script opens the
    // stored night on or before a picked day.
    [Fact]
    public async Task TonightsPageAndTheRunPageDrawTheCalendarOverTheNightsTheStoreHolds()
    {
        using var store = await FixtureExpectations.WithListings();

        var (_, newest) = await TwoNights(store);
        var held = new List<string>();

        using (var connection = new SqliteConnection($"Data Source={store.DatabaseFile};Mode=ReadOnly"))
        {
            connection.Open();

            using var command = connection.CreateCommand();

            command.CommandText = "SELECT DISTINCT session_date FROM listing ORDER BY session_date;";

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                held.Add(reader.GetString(0));
            }
        }

        Assert.True(held.Count >= 2, $"the store holds {held.Count} night(s), expected at least 2.");
        Assert.Equal(Stamp(newest), held[^1]);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        foreach (var (route, page) in new[] { (SinglePageApp.NightRoute, "/screens/tonight/"), (SinglePageApp.RunRoute, "/screens/run/") })
        {
            var drawn = await client.GetStringAsync(page + Stamp(newest));

            Assert.Contains($"<span class=\"night-picker\" data-route=\"{route}\" data-night=\"{Stamp(newest)}\">", drawn, StringComparison.Ordinal);
            Assert.Contains($"min=\"{held[0]}\" max=\"{held[^1]}\" data-nights=\"{string.Join(' ', held)}\"", drawn, StringComparison.Ordinal);
            Assert.Contains($"data-move=\"earlier\" href=\"{route}{held[^2]}\"", drawn, StringComparison.Ordinal);
        }

        var shell = await client.GetStringAsync("/");

        Assert.Contains("closest('input.np-date')", shell, StringComparison.Ordinal);
        Assert.Contains("held.filter((night) => night <= picked.value)", shell, StringComparison.Ordinal);
    }
}
