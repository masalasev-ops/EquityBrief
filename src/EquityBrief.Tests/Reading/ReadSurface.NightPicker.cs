using EquityBrief.Tests.Checks;
using EquityBrief.Web.App;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Reading;

// Tonight's page and the run page each carry a calendar over the nights they draw, with the stored night
// before and after the one drawn beside it, so an earlier night is a click away rather than a route typed
// by hand, and a day holding a night is told from one holding none at a glance.
public partial class ReadSurface
{
    // Worked by hand over five stored nights, the last day of August and a Thursday and a Friday and the
    // Monday and Tuesday after them in September. The Monday's night before is the Friday, across the
    // weekend no night was stored for, and its night after is the Tuesday. The calendar holds September
    // first and then August, each seven columns from Monday: September 2026 opens on a Tuesday, so one
    // empty cell comes before its first day, and August 2026 on a Saturday, five. Each stored night is an underlined link to
    // its own route, the Monday marked as the night drawn, and every other day is no link. The first night
    // has none before it, the last none after it and no link to the newest, the nights read the same in any
    // order handed in, and a store holding no night draws no calendar.
    [Fact]
    public void ADatedScreensCalendarOffersTheStoredNightsAndTheNightsEitherSide()
    {
        DateOnly[] held = [new(2026, 8, 31), new(2026, 9, 17), new(2026, 9, 18), new(2026, 9, 21), new(2026, 9, 22)];

        var monday = Cards.NightPicker(held[3], held, SinglePageApp.NightRoute, "#/");

        Assert.Contains("<span class=\"night-picker\" data-route=\"#/night/\" data-night=\"2026-09-21\">", monday, StringComparison.Ordinal);
        Assert.Contains("data-move=\"earlier\" href=\"#/night/2026-09-18\"", monday, StringComparison.Ordinal);
        Assert.Contains("data-move=\"later\" href=\"#/night/2026-09-22\"", monday, StringComparison.Ordinal);
        Assert.Contains("<summary class=\"np-date\" aria-label=\"Choose a night to view\">2026-09-21</summary>", monday, StringComparison.Ordinal);
        Assert.Contains("<a class=\"np-newest\" href=\"#/\">", monday, StringComparison.Ordinal);

        // The months, newest first, and where each opens in its first week.
        Assert.Equal(
            ["2026-09", "2026-08"],
            System.Text.RegularExpressions.Regex.Matches(monday, "<div class=\"np-month\" data-month=\"([0-9-]+)\">").Select(match => match.Groups[1].Value));
        const string Week = "<span class=\"np-wd\">Mo</span><span class=\"np-wd\">Tu</span><span class=\"np-wd\">We</span><span class=\"np-wd\">Th</span><span class=\"np-wd\">Fr</span><span class=\"np-wd\">Sa</span><span class=\"np-wd\">Su</span>";
        const string Pad = "<span class=\"np-pad\"></span>";

        Assert.Contains($"<div class=\"np-caption\">September 2026</div><div class=\"np-grid\">{Week}{Pad}<span class=\"np-off\">1</span>", monday, StringComparison.Ordinal);
        Assert.Contains($"<div class=\"np-caption\">August 2026</div><div class=\"np-grid\">{Week}{Pad}{Pad}{Pad}{Pad}{Pad}<span class=\"np-off\">1</span>", monday, StringComparison.Ordinal);

        // The stored nights are links, the night drawn marked, and the days around them are not.
        Assert.Equal(
            ["2026-08-31", "2026-09-17", "2026-09-18", "2026-09-21", "2026-09-22"],
            System.Text.RegularExpressions.Regex.Matches(monday, "<a class=\"np-day\" href=\"#/night/([0-9-]+)\" data-night=\"\\1\"").Select(match => match.Groups[1].Value).Order(StringComparer.Ordinal));
        Assert.Contains("<a class=\"np-day\" href=\"#/night/2026-09-21\" data-night=\"2026-09-21\" aria-current=\"date\">21</a>", monday, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(monday, "aria-current=\"date\""));
        Assert.Contains("<span class=\"np-off\">19</span><span class=\"np-off\">20</span>", monday, StringComparison.Ordinal);

        // Handed in any order, the nights read the same.
        Assert.Equal(monday, Cards.NightPicker(held[3], [.. held.Reverse()], SinglePageApp.NightRoute, "#/"));

        var first = Cards.NightPicker(held[0], held, SinglePageApp.RunRoute, SinglePageApp.RunRoute);

        Assert.Contains("<span class=\"np-move\" data-move=\"earlier\" aria-disabled=\"true\">", first, StringComparison.Ordinal);
        Assert.Contains("data-move=\"later\" href=\"#/run/2026-09-17\"", first, StringComparison.Ordinal);
        Assert.Contains("<a class=\"np-day\" href=\"#/run/2026-09-22\" data-night=\"2026-09-22\">22</a>", first, StringComparison.Ordinal);

        var last = Cards.NightPicker(held[4], held, SinglePageApp.NightRoute, "#/");

        Assert.Contains("data-move=\"earlier\" href=\"#/night/2026-09-21\"", last, StringComparison.Ordinal);
        Assert.Contains("<span class=\"np-move\" data-move=\"later\" aria-disabled=\"true\">", last, StringComparison.Ordinal);
        Assert.DoesNotContain("np-newest", last, StringComparison.Ordinal);

        // A store holding no night draws no calendar.
        Assert.Empty(Cards.NightPicker(held[0], [], SinglePageApp.NightRoute, "#/"));
    }

    // Served over a store holding two nights and listed by the reasons on both, both dated screens draw the
    // calendar on their own route over the nights the listings hold, read off the store itself.
    [Fact]
    public async Task TonightsPageAndTheRunPageDrawTheCalendarOverTheNightsTheStoreHolds()
    {
        using var store = await FixtureExpectations.WithListings();

        var (_, newest) = await TwoNights(store);

        store.Execute("DELETE FROM list_rule WHERE rule = 'filter';");

        var held = Held(store);

        Assert.True(held.Count >= 2, $"the store holds {held.Count} night(s), expected at least 2.");
        Assert.Equal(Stamp(newest), held[^1]);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        foreach (var (route, page) in new[] { (SinglePageApp.NightRoute, "/screens/tonight/"), (SinglePageApp.RunRoute, "/screens/run/") })
        {
            var drawn = await client.GetStringAsync(page + Stamp(newest));

            Assert.Contains($"<span class=\"night-picker\" data-route=\"{route}\" data-night=\"{Stamp(newest)}\">", drawn, StringComparison.Ordinal);
            Assert.Contains($"data-move=\"earlier\" href=\"{route}{held[^2]}\"", drawn, StringComparison.Ordinal);
            Assert.Equal(
                held,
                System.Text.RegularExpressions.Regex.Matches(drawn, $"<a class=\"np-day\" href=\"{System.Text.RegularExpressions.Regex.Escape(route)}([0-9-]+)\" data-night=\"\\1\"").Select(match => match.Groups[1].Value).Order(StringComparer.Ordinal));
        }
    }

    // On a store the swing filter has listed, both dated screens open from its first night. Over the two
    // nights, the newest listed by the filter: the night before it draws, on either screen, the line saying
    // the record starts on the newest and a link opening it, and no page of the evening; the newest draws
    // its page with a calendar holding the newest alone; and read with the filter's night taken out, the
    // store is one the filter never listed, and the night before draws its own page again.
    // see: The dated screens open from the swing filter's first night, and an evening before it is not drawn
    [Fact]
    public async Task TheDatedScreensOpenFromTheSwingFiltersFirstNight()
    {
        using var store = await FixtureExpectations.WithListings();

        var (earlier, newest) = await TwoNights(store);

        store.Execute("DELETE FROM list_rule;");
        store.Execute($"INSERT INTO list_rule (session_date, rule) VALUES ('{Stamp(newest)}', 'filter');");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        foreach (var (route, page, section) in new[] { (SinglePageApp.NightRoute, "/screens/tonight/", "tonight"), (SinglePageApp.RunRoute, "/screens/run/", "run") })
        {
            var before = await client.GetStringAsync(page + Stamp(earlier));

            Assert.Contains($"<section class=\"before-the-record\" data-night=\"{Stamp(earlier)}\" data-first=\"{Stamp(newest)}\">", before, StringComparison.Ordinal);
            Assert.Contains($"The record starts on {Stamp(newest)}, the swing filter's first night, so {Stamp(earlier)} is not drawn. <a href=\"{route}{Stamp(newest)}\">Open {Stamp(newest)}</a>", before, StringComparison.Ordinal);
            Assert.DoesNotContain($"<section class=\"{section}\"", before, StringComparison.Ordinal);

            var first = await client.GetStringAsync(page + Stamp(newest));

            Assert.Contains($"<section class=\"{section}\" data-night=\"{Stamp(newest)}\"", first, StringComparison.Ordinal);
            Assert.Equal(
                [Stamp(newest)],
                System.Text.RegularExpressions.Regex.Matches(first, "<a class=\"np-day\" href=\"[^\"]*\" data-night=\"([0-9-]+)\"").Select(match => match.Groups[1].Value));
        }

        store.Execute("DELETE FROM list_rule WHERE rule = 'filter';");

        Assert.Contains($"<section class=\"tonight\" data-night=\"{Stamp(earlier)}\"", await client.GetStringAsync($"/screens/tonight/{Stamp(earlier)}"), StringComparison.Ordinal);
        Assert.Contains($"<section class=\"run\" data-night=\"{Stamp(earlier)}\"", await client.GetStringAsync($"/screens/run/{Stamp(earlier)}"), StringComparison.Ordinal);
    }

    // The nights the listings hold, oldest first, read by a path that is not the read surface.
    static List<string> Held(TemporaryStore store)
    {
        var held = new List<string>();

        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile};Mode=ReadOnly");
        connection.Open();

        using var command = connection.CreateCommand();

        command.CommandText = "SELECT DISTINCT session_date FROM listing ORDER BY session_date;";

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            held.Add(reader.GetString(0));
        }

        return held;
    }
}
