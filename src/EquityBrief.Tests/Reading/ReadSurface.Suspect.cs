using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Checks;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
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

    // MOG-A's and HOS's lines of 2026-11-04, whose refetches each succeeded, read from the reason the check stores: each says
    // what the provider's answer held and neither says a download failed; a reason the check wrote from a failure says one
    // did; and past the night's limit, where nothing was asked, neither is claimed.
    // see: A suspect name's line says what the provider's answer held, and says a download failed only where one did
    [Fact]
    public void ASuspectLineSaysWhatTheProvidersAnswerHeldAndAFailedDownloadOnlyWhereOneFailed()
    {
        const string At = "2026-11-05T00:00:41Z";
        const string Failed = "downloading its year of prices again failed";

        static string Said(string ticker, SuspectPrices suspect) => WebUtility.HtmlDecode(new EquityBrief.Web.Marks.MarkRenderer().PricesSuspect(ticker, suspect));

        DateOnly[] sessions = [new(2026, 7, 21), new(2026, 7, 22), new(2026, 7, 31)];
        var copied = Said("MOG-A", new SuspectPrices(At, SuspectReason.Copied(sessions), sessions));

        Assert.Contains("The provider's prices for 2026-07-21, 2026-07-22 and 2026-07-31 are copies of the session before, with no trades. They are asked for again on the next five nights and weekly after.", copied, StringComparison.Ordinal);
        Assert.DoesNotContain(Failed, copied, StringComparison.Ordinal);

        Assert.Contains(
            "The provider's prices for 2026-10-26 are copies",
            Said("MSGR", new SuspectPrices(At, SuspectReason.Copied([new(2026, 10, 26)]), [new(2026, 10, 26)])),
            StringComparison.Ordinal);

        var behind = Said("HOS", new SuspectPrices(At, SuspectReason.Behind(new(2026, 11, 3))));

        Assert.Contains($"The provider held nothing for HOS after 2026-11-03 when it was asked at {At}. It is asked for again on the next five nights and weekly after.", behind, StringComparison.Ordinal);
        Assert.DoesNotContain(Failed, behind, StringComparison.Ordinal);
        Assert.DoesNotContain("dividend or split", behind, StringComparison.Ordinal);

        var failed = Said("AAPL", new SuspectPrices(At, "The request timed out"));

        Assert.Contains($"AAPL's prices may not reflect a recent dividend or split: {Failed}. Last tried {At}, because The request timed out.", failed, StringComparison.Ordinal);

        // Named by the night's limit it was never asked for.
        foreach (var unasked in new[]
        {
            Said("HOS", new SuspectPrices(At, SuspectReason.PastTheLimit(20, SuspectReason.Behind(new(2026, 11, 3))))),
            Said("MSGR", new SuspectPrices(At, SuspectReason.PastTheLimit(20, SuspectReason.Copied([new(2026, 10, 26)])), [new(2026, 10, 26)])),
        })
        {
            Assert.Contains("Past the 20 members a night asks for", unasked, StringComparison.Ordinal);
            Assert.DoesNotContain(Failed, unasked, StringComparison.Ordinal);
            Assert.DoesNotContain("when it was asked", unasked, StringComparison.Ordinal);
        }

        Assert.Null(SuspectReason.Read(null));
        Assert.Null(SuspectReason.Read("The request timed out"));
    }

    // The days a line names are the stored copied candles alone, a day of no volume whose prices moved not among them, and the
    // row beside the name says the same in a few words.
    [Fact]
    public async Task ASuspectNamesCopiedDaysAreReadFromItsStoredBarsAndItsRowSaysSo()
    {
        using var store = new TemporaryStore().Migrated();

        store.Execute(
            "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES " +
            "('MOGA', '2026-07-20', '390', '396', '389', '395.68', 100, 'bulk', 'x', '395.68'), " +
            "('MOGA', '2026-07-21', '395.68', '395.68', '395.68', '395.68', 0, 'bulk', 'x', '395.68'), " +
            "('MOGA', '2026-07-22', '395.68', '395.68', '395.68', '395.68', 0, 'bulk', 'x', '395.68'), " +
            "('MOGA', '2026-07-23', '396', '401', '395', '400', 0, 'bulk', 'x', '400'), " +
            "('MOGA', '2026-07-24', '400', '402', '399', '401', 100, 'bulk', 'x', '401');");

        store.Execute(
            "INSERT INTO series_state (ticker, state, reason, checked_at, retries) VALUES " +
            $"('MOGA', 'suspect', '{SuspectReason.Copied([new(2026, 7, 21), new(2026, 7, 22)])}', '2026-11-05T00:00:41Z', 0), " +
            $"('HOS', 'suspect', '{SuspectReason.Behind(new(2026, 11, 3))}', '2026-11-05T00:00:41Z', 1);");

        var rows = await new ReadApi(store.DatabaseFile, FixedClock.At(new DateTimeOffset(2026, 11, 5, 3, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates)).SuspectSeriesAsync();

        Assert.Null(rows.Single(row => row.Ticker == "HOS").Copied);
        Assert.Equal([new DateOnly(2026, 7, 21), new DateOnly(2026, 7, 22)], rows.Single(row => row.Ticker == "MOGA").Copied);

        var line = new EquityBrief.Web.Marks.MarkRenderer().PricesSuspect("MOGA", NameScreen.Suspect(rows.Single(row => row.Ticker == "MOGA")));

        Assert.Contains("The provider's prices for 2026-07-21 and 2026-07-22 are copies", line, StringComparison.Ordinal);
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
