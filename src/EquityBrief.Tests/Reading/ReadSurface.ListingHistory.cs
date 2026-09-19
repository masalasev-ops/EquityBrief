using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, the 5.8 correction: a name's listing history on its page, section 15.9's row.
public partial class ReadSurface
{
    static async Task<string> NamePageWithHistory(TemporaryStore store, string ticker)
    {
        var api = Api(store);

        return NameScreen.Region(
            new SinglePageApp(),
            new MarkRenderer(),
            ticker,
            await api.BarsAsync(ticker, DateOnly.MinValue, DateOnly.MaxValue),
            await api.IndicatorsAsync(ticker, DateOnly.MinValue, DateOnly.MaxValue),
            await api.LevelsAsync(ticker),
            await api.ProfileAsync(ticker),
            await api.LadderAsync(ticker),
            await api.NextEventAsync(ticker, DateOnly.MinValue),
            await api.MovesAsync(ticker),
            await api.FundamentalsAsync(ticker),
            history: await api.ListingsAsync(ticker, UniverseScreen.StripSessions),
            outcomes: await api.ForwardReturnsAsync(ticker));
    }

    [Fact]
    public async Task ANamesListingHistoryStatesWhatFollowedEachEveningItWasListedBesideTheBaseRateAndFormsNoRateForTheName()
    {
        // Over the fixture's night, MSFT is given two earlier evenings: one it was listed on, whose
        // five-session horizon has matured to a win beside a base rate of 40 per cent and whose
        // twenty-one has not, and one it was not.
        using var store = await FixtureReplay.ReplayedAsync();

        const string Reasons = "[{\"name\":\"at entry zone\",\"fired\":true,\"values\":{\"close\":\"1.00\"}},{\"name\":\"crossed a level\",\"fired\":false,\"values\":{}}]";
        const string Quiet = "[{\"name\":\"at entry zone\",\"fired\":false,\"values\":{}}]";

        store.Execute(
            "INSERT INTO listing (ticker, session_date, reasons, fired_count, plan_at_listing, shadow_reasons) VALUES " +
            $"('MSFT', '2026-09-04', '{Reasons}', 1, '[]', '[]'), ('MSFT', '2026-09-03', '{Quiet}', 0, '[]', '[]');");
        store.Execute(
            "INSERT INTO forward_return (ticker, session_date, horizon, outcome, resolved_on, return_pct, base_rate, break_even) VALUES " +
            "('MSFT', '2026-09-04', '5', 'win', '2026-09-11', 2.5, 40.0, NULL), " +
            "('MSFT', '2026-09-04', '21', NULL, NULL, NULL, 40.0, NULL);");

        var page = await NamePageWithHistory(store, "MSFT");
        var card = Regex.Match(page, "<section class=\"listing-history\" data-ticker=\"MSFT\" data-sessions=\"(\\d+)\" data-evenings=\"(\\d+)\">(.*?)</section>", RegexOptions.Singleline);

        Assert.True(card.Success, "the listing history is not drawn");

        // The strip over every stored evening, and one row for each the name was listed on, the
        // counts read off the store by a query of the test's own.
        var stored = Rows(store, "SELECT session_date, fired_count FROM listing WHERE ticker = 'MSFT' ORDER BY session_date DESC;");
        var listed = stored.Where(row => row[1] != "0").Select(row => row[0]).ToArray();

        Assert.Equal(3, stored.Count);
        Assert.Contains("2026-09-04", listed);
        Assert.DoesNotContain("2026-09-03", listed);
        Assert.Equal($"{stored.Count}", card.Groups[1].Value);
        Assert.Equal($"{listed.Length}", card.Groups[2].Value);
        Assert.Contains($"data-evenings=\"{stored.Count}\" data-listed=\"{listed.Length}\"", card.Groups[3].Value, StringComparison.Ordinal);
        Assert.Equal(listed, Regex.Matches(card.Groups[3].Value, "<tr data-evening=\"([^\"]+)\"").Select(match => match.Groups[1].Value));

        // The earlier evening: the reason that fired, the close the store holds for that session
        // rather than a value the reason carried, and its matured horizon beside the base rate its
        // row carries, the stored figures whole on the row.
        var close = decimal.Parse(Rows(store, "SELECT close FROM bar WHERE ticker = 'MSFT' AND session_date = '2026-09-04';").Single()[0], CultureInfo.InvariantCulture);
        var row = Regex.Match(card.Groups[3].Value, "<tr data-evening=\"2026-09-04\"[^>]*>.*?</tr>", RegexOptions.Singleline).Value;

        Assert.Contains($"data-close=\"{close.ToString(CultureInfo.InvariantCulture)}\"", row, StringComparison.Ordinal);
        Assert.Contains("data-outcome-5=\"win\" data-return-5=\"2.5\" data-base-rate-5=\"40\"", row, StringComparison.Ordinal);
        Assert.Contains("<td>at entry zone</td>", row, StringComparison.Ordinal);
        Assert.Contains($"<td class=\"num\">{Figures.Price(close)}</td>", row, StringComparison.Ordinal);
        Assert.Contains("<td>+2.50%, a win; base rate 40.0%</td>", row, StringComparison.Ordinal);

        // A horizon too recent to have matured says so rather than drawing a blank or a zero.
        Assert.Contains("<td>not yet matured</td>", row, StringComparison.Ordinal);
        Assert.DoesNotContain("<td></td>", card.Groups[3].Value, StringComparison.Ordinal);

        // And no rate for the name: a result is drawn only in its own evening's cell, each beside
        // its base rate, and nothing sums the evenings.
        Assert.Single(Regex.Matches(card.Groups[3].Value, "a (win|loss);"));
        Assert.Single(Regex.Matches(card.Groups[3].Value, "base rate \\d"));
        Assert.DoesNotContain("<tfoot", card.Groups[3].Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANameNotListedInTheWindowSaysSoUnderItsStrip()
    {
        using var store = await FixtureReplay.ReplayedAsync();

        Assert.Equal(["0"], Rows(store, "SELECT fired_count FROM listing WHERE ticker = 'NFLX';").Select(row => row[0]));

        var page = await NamePageWithHistory(store, "NFLX");

        Assert.Contains("<section class=\"listing-history\" data-ticker=\"NFLX\" data-sessions=\"1\" data-evenings=\"0\">", page, StringComparison.Ordinal);
        Assert.Contains("<p class=\"listing-none\">NFLX was not on the list on any of these sessions.</p>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("listing-evenings", page, StringComparison.Ordinal);
    }
}
