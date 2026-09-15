using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Checks;
using EquityBrief.Web.App;
using EquityBrief.Worker.Bars;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Reading;

// read-surface, the 7.0 ruling: a name whose stored series is suspect is said so on its name page,
// on its row on tonight's list and in its exported report, from the row as the corporate action
// check stored it, and nothing is drawn for a name whose series is trusted. Over a store the whole
// pipeline populated, through the routes the three are served from.
// see: A suspect name is asked for again on the five nights after it is marked and weekly after that, and its own page, its row on tonight's list and the run page say so until a refetch succeeds
public partial class ReadSurface
{
    [Fact]
    public async Task ASuspectNameIsSaidSoOnItsPageItsRowOnTonightsListAndItsExportedReportAndATrustedNameIsNot()
    {
        using var store = await FixtureExpectations.WithListings();

        var night = NightIn(store);
        var fired = FiredNamesOn(store, night);

        Assert.True(fired.Count >= 2, $"The night lists {fired.Count} name(s), fewer than the two this reads.");

        var (suspect, trusted) = (fired[0], fired[1]);

        // Written in the columns the check writes, one name suspect with its nightly retries
        // spent and one trusted, the reason carrying a character the page has to escape.
        var reason = $"No captured response for {suspect} & its year";

        store.Execute(
            "INSERT INTO series_state (ticker, state, reason, checked_at, retries) VALUES " +
            $"('{suspect}', 'suspect', '{reason}', '2026-09-01T21:10:00Z', 6), " +
            $"('{trusted}', 'ok', NULL, '2026-09-01T21:10:00Z', 0) " +
            "ON CONFLICT (ticker) DO UPDATE SET state = excluded.state, reason = excluded.reason, " +
            "checked_at = excluded.checked_at, retries = excluded.retries;");

        // The read hands the row back as stored, by the state the check itself writes.
        Assert.Equal(CorporateActionChecker.Suspect, ReadApi.SuspectState);
        Assert.Equal(
            new SuspectSeriesRow(suspect, reason, "2026-09-01T21:10:00Z", 6),
            Assert.Single(await new ReadApi(store.DatabaseFile, FixedClock.At(new DateTimeOffset(2026, 9, 8, 21, 10, 0, TimeSpan.Zero), SessionZones.UnitedStates)).SuspectSeriesAsync()));

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        // The suspect name's row says so beside the name, with the instant and the reason as
        // its title, and the trusted name's row does not.
        var list = await client.GetStringAsync($"/screens/tonight/{night}");
        var suspectRow = RowOf(list, suspect);

        Assert.Contains(">prices may not reflect a dividend or split</span>", suspectRow, StringComparison.Ordinal);
        Assert.Contains("title=\"last tried 2026-09-01T21:10:00Z, because " + WebUtility.HtmlEncode(reason) + "\"", suspectRow, StringComparison.Ordinal);
        Assert.DoesNotContain("prices-suspect", RowOf(list, trusted), StringComparison.Ordinal);

        // The name page and the file open with the line, the reason escaped in the markup and
        // stated whole once read.
        var page = await client.GetStringAsync($"/screens/name/{suspect}");
        var file = await client.GetStringAsync(ReportExporter.Route + suspect);

        foreach (var surface in new[] { page, file })
        {
            var line = Assert.Single(Blocks(surface, "<p class=\"prices-suspect\"[^>]*>.*?</p>"));

            Assert.Contains("&amp; its year", line, StringComparison.Ordinal);
            Assert.Contains($"{suspect}'s prices may not reflect a recent dividend or split", WebUtility.HtmlDecode(line), StringComparison.Ordinal);
            Assert.Contains($"Last tried 2026-09-01T21:10:00Z, because {reason}.", WebUtility.HtmlDecode(line), StringComparison.Ordinal);
        }

        // First in the name's region, above every figure the page draws from those prices.
        Assert.StartsWith(
            $"<section class=\"name\" data-ticker=\"{suspect}\"><p class=\"prices-suspect\"",
            page[page.IndexOf("<section class=\"name\"", StringComparison.Ordinal)..],
            StringComparison.Ordinal);

        Assert.DoesNotContain("prices-suspect", await client.GetStringAsync($"/screens/name/{trusted}"), StringComparison.Ordinal);
    }

    // Every name that fired on one night, read off the store.
    static IReadOnlyList<string> FiredNamesOn(TemporaryStore store, string night)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ticker FROM listing WHERE session_date = $n AND fired_count > 0 ORDER BY ticker;";
        command.Parameters.AddWithValue("$n", night);

        var tickers = new List<string>();

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            tickers.Add(reader.GetString(0));
        }

        return tickers;
    }

    // One row of tonight's list, by its ticker.
    static string RowOf(string list, string ticker) =>
        Assert.Single(Blocks(list, $"<tr data-ticker=\"{Regex.Escape(ticker)}\".*?</tr>"));
}
