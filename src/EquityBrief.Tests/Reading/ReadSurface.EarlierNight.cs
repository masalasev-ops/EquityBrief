using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Reading;

// read-surface, the 5.8 correction: a name's page for an earlier night, section 15.9's second
// route. Every read the page makes is bounded by that night, the evening drawn is the newest the
// listings hold on or before the date asked for, and the page says which evening it drew.
// see: A name's page for an earlier night is what the store held that night
public partial class ReadSurface
{
    // A session the store holds a bar for and no listing on, so an evening can be written on it
    // without standing on one the replay already wrote.
    static string SessionWithoutAnEvening(TemporaryStore store, string ticker, string night) =>
        Rows(
            store,
            "SELECT b.session_date FROM bar b WHERE b.ticker = '" + ticker + "' AND b.session_date < '" + night + "' " +
            "AND NOT EXISTS (SELECT 1 FROM listing l WHERE l.ticker = b.ticker AND l.session_date = b.session_date) " +
            "ORDER BY b.session_date DESC LIMIT 1;")
            .Single()[0];

    [Fact]
    public async Task ANamesPageForAnEarlierNightDrawsTheFiguresThatNightComputedAndOffersNoControl()
    {
        using var store = await FixtureExpectations.WithListings();

        var night = NightIn(store);
        var fired = FiredNamesOn(store, night);
        var name = fired[0];
        var earlier = SessionWithoutAnEvening(store, name, night);

        // That evening written into the store: the name on the list for one reason, its own
        // trend state, and a band at prices no other evening holds. A second name is listed
        // on it as well, so the name has a neighbour and the walk has something to link.
        const string Reasons = "[{\"name\":\"at entry zone\",\"fired\":true,\"values\":{\"close\":\"1.00\"}}]";

        store.Execute(
            $"INSERT INTO listing (ticker, session_date, reasons, fired_count, plan_at_listing, shadow_reasons) VALUES ('{name}', '{earlier}', '{Reasons}', 1, '[]', '[]'), ('{fired[1]}', '{earlier}', '{Reasons}', 1, '[]', '[]');" +
            $"INSERT INTO ladder (ticker, as_of, trend_state, plan) SELECT ticker, '{earlier}', 'downtrend', plan FROM ladder WHERE ticker = '{name}' AND as_of = '{night}';" +
            "INSERT INTO level (ticker, as_of, low_edge, high_edge, role, immediate, strength, has_non_average_anchor, members) " +
            $"SELECT ticker, '{earlier}', '11.11', '22.22', role, immediate, strength, has_non_average_anchor, members FROM level WHERE ticker = '{name}' AND as_of = '{night}' LIMIT 1;");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = await client.GetStringAsync($"/screens/name/{name}/{earlier}");
        var tonight = await client.GetStringAsync($"/screens/name/{name}");

        // It says which evening it is, above every figure, and links to tonight's page.
        Assert.Contains(
            $"<p class=\"notice earlier-night\" data-night=\"{earlier}\" role=\"status\">This is {name} as the store held it after the close of {earlier}.",
            page,
            StringComparison.Ordinal);
        Assert.Contains($"<a href=\"#/name/{name}\">Tonight's page</a>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("earlier-night", tonight, StringComparison.Ordinal);

        // The figures are that evening's: its close, its trend state and its band, and none of
        // tonight's, which the same page for tonight still draws.
        var tonightsTrend = Regex.Match(tonight, "data-trend-state=\"([a-z_]+)\" data-as-of=\"([0-9-]+)\"");

        Assert.Equal(night, tonightsTrend.Groups[2].Value);
        Assert.NotEqual("downtrend", tonightsTrend.Groups[1].Value);
        Assert.Contains($"data-trend-state=\"downtrend\" data-as-of=\"{earlier}\"", page, StringComparison.Ordinal);
        Assert.Contains($"As of the close of {earlier}", page, StringComparison.Ordinal);
        Assert.Contains($"As of the close of {night}", tonight, StringComparison.Ordinal);
        Assert.Contains("data-low-edge=\"11.11\" data-high-edge=\"22.22\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("data-low-edge=\"11.11\"", tonight, StringComparison.Ordinal);

        // The chart stops at that evening, matched against the store's own last bar on or
        // before it rather than against the date asked for.
        var drawn = Regex.Matches(page, "<svg[^>]*class=\"level-chart\"[^>]*>").Count;
        var candles = Regex.Matches(page, "<g class=\"candle\" data-session=\"([0-9-]+)\"")
            .Select(candle => candle.Groups[1].Value)
            .ToArray();

        Assert.True(drawn > 0, "the page draws no chart");
        Assert.Equal(Rows(store, $"SELECT MAX(session_date) FROM bar WHERE ticker = '{name}' AND session_date <= '{earlier}';").Single()[0], candles.Max(StringComparer.Ordinal));

        // Why it is here is that evening's listing, named by its date rather than by tonight.
        Assert.Contains($"On the list on {earlier} for these reasons", page, StringComparison.Ordinal);

        // And nothing on it asks for research: a pass writes about the company now, and what
        // research has cost is not this evening's statement.
        Assert.DoesNotContain("research-control", page, StringComparison.Ordinal);
        Assert.DoesNotContain("research-cost", page, StringComparison.Ordinal);
        Assert.Contains("research-control", tonight, StringComparison.Ordinal);

        // And the evening is reached from the listing history on tonight's page, which is
        // where a reader finds an earlier night at all.
        Assert.Contains($"<td><a href=\"#/name/{name}/{earlier}\">{earlier}</a></td>", tonight, StringComparison.Ordinal);

        // The walk stays in the evening, so a pass through that evening's list is one pass.
        // The neighbour is what makes this an assertion rather than an empty loop, and the
        // second listing above is why there is one.
        var walk = Regex.Match(page, "<nav class=\"walk\"[^>]*>(.*?)</nav>", RegexOptions.Singleline);
        var walked = Regex
            .Matches(walk.Groups[1].Value, "href=\"#/name/([^\"]+)\"")
            .Select(neighbour => neighbour.Groups[1].Value)
            .ToArray();

        Assert.True(walk.Success, "the page draws no walk");
        Assert.NotEmpty(walked);
        Assert.All(walked, neighbour => Assert.EndsWith($"/{earlier}", neighbour, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnEveningTheListingsDoNotHoldIsDrawnAsTheOneBeforeItAndADateThatIsNotOneIsTonight()
    {
        using var store = await FixtureExpectations.WithListings();

        var night = NightIn(store);
        var name = FiredNamesOn(store, night)[0];

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        // A day the listings hold no evening on, which the test reads off the store rather
        // than assuming, is drawn as the newest evening on or before it.
        var quiet = DateOnly.ParseExact(night, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        Assert.Empty(Rows(store, $"SELECT session_date FROM listing WHERE session_date = '{quiet}';"));
        Assert.Contains(
            $"<p class=\"notice earlier-night\" data-night=\"{night}\"",
            await client.GetStringAsync($"/screens/name/{name}/{quiet}"),
            StringComparison.Ordinal);

        // And something that is not a date is tonight's page with a line saying what was
        // asked for, as an unknown route is tonight's list with one.
        var asked = await client.GetStringAsync($"/screens/name/{name}/2026-13-40");

        Assert.Contains("<p class=\"notice\" role=\"status\" data-not-a-night=\"2026-13-40\">2026-13-40 is not an evening this reads, so this is tonight.</p>", asked, StringComparison.Ordinal);
        Assert.Contains($"As of the close of {night}", asked, StringComparison.Ordinal);
        Assert.DoesNotContain("earlier-night", asked, StringComparison.Ordinal);
    }
}
