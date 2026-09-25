using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Tests.Checks;
using EquityBrief.Core.Indicators;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Reading;

// read-surface: an earlier night on tonight's list is drawn from what that night stored, through
// the route that serves it, over a store holding two nights whose plans and bands disagree.
public partial class ReadSurface
{
    [Fact]
    public async Task AnEarlierNightIsOrderedByThePlansThatNightStored()
    {
        using var store = await FixtureExpectations.WithListings();

        var (earlier, newest) = await TwoNights(store);
        var (first, last) = BandedNames(store, newest);

        // The two names fire the same reasons on both nights, so the plan's reward to risk is what
        // orders them. On the earlier night the name that sorts last by ticker has the higher, and
        // on the newest night the lower, so the right order is the reverse of the ticker order and
        // neither the newest plans nor the ticker tiebreak can produce it. The bands say the
        // opposite of the plans on both nights, so an order read off them is told apart as well.
        FireAlike(store, earlier, first, last);
        FireAlike(store, newest, first, last);

        SetPlan(store, earlier, first, "120");
        SetPlan(store, earlier, last, "130");
        SetPlan(store, newest, first, "130");
        SetPlan(store, newest, last, "120");

        SetStrength(store, earlier, first, 9);
        SetStrength(store, earlier, last, 1);
        SetStrength(store, newest, first, 1);
        SetStrength(store, newest, last, 9);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var then = await client.GetStringAsync($"/screens/tonight/{Stamp(earlier)}");

        Assert.Contains("data-reward-to-risk=\"2.5\"", RowOf(then, last), StringComparison.Ordinal);
        Assert.Contains("data-reward-to-risk=\"1.5\"", RowOf(then, first), StringComparison.Ordinal);
        Assert.True(
            RowAt(then, last) < RowAt(then, first),
            $"{Stamp(earlier)} drew {first} before {last}, where that night's plans put {last} first.");

        // The counter-reading over the same store: the newest night, whose plans say the
        // opposite, draws the opposite order, so the two nights can be told apart.
        var now = await client.GetStringAsync($"/screens/tonight/{Stamp(newest)}");

        Assert.True(
            RowAt(now, first) < RowAt(now, last),
            $"{Stamp(newest)} drew {last} before {first}, where that night's plans put {first} first.");
    }

    [Fact]
    public async Task ANamePagesWalkFollowsTheListOfTheNightItWalksWhenNewerPlansAreStored()
    {
        using var store = await FixtureExpectations.WithListings();

        var api = Api(store);
        var listed = (await api.NewestNightAsync())!.Value;
        var after = listed.AddDays(1);
        var (first, last) = BandedNames(store, listed);

        // Every name listed that night fires alike, so the plan's reward to risk alone orders the
        // list. The night listed puts the first name by ticker at the top and the last at the
        // bottom, and a plan and band set stored after it, by a night that stopped before it
        // listed, says the reverse.
        var on = Stamp(listed);
        var fired = Strings(store, $"SELECT CAST(MAX(fired_count) AS TEXT) FROM listing WHERE session_date = '{on}';").Single();

        store.Execute(
            "UPDATE listing SET " +
            $"reasons = (SELECT reasons FROM listing WHERE session_date = '{on}' ORDER BY fired_count DESC, ticker LIMIT 1), " +
            $"fired_count = {fired}, plan_at_listing = '{PlanWith("125")}' WHERE session_date = '{on}';");

        SetPlan(store, listed, first, "135");
        SetPlan(store, listed, last, "115");

        Insert(
            store,
            "INSERT INTO ladder (ticker, as_of, trend_state, plan) " +
            $"SELECT ticker, '{Stamp(after)}', trend_state, plan FROM ladder WHERE as_of = '{on}';");
        Insert(
            store,
            "INSERT INTO level (ticker, as_of, low_edge, high_edge, role, immediate, strength, has_non_average_anchor, members) " +
            $"SELECT ticker, '{Stamp(after)}', low_edge, high_edge, role, immediate, strength, has_non_average_anchor, members " +
            $"FROM level WHERE as_of = '{on}';");

        store.Execute($"UPDATE ladder SET plan = json_set(plan, '$.arithmetic.firstRewardToRisk', '9.0000') WHERE ticker = '{last}' AND as_of = '{Stamp(after)}';");
        store.Execute($"UPDATE ladder SET plan = json_set(plan, '$.arithmetic.firstRewardToRisk', '0.1000') WHERE ticker = '{first}' AND as_of = '{Stamp(after)}';");
        SetStrength(store, after, first, 1);
        SetStrength(store, after, last, 99);

        Assert.Equal(listed, (await api.NewestNightAsync())!.Value);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var order = Regex.Matches(await client.GetStringAsync("/screens/tonight"), "<tr data-ticker=\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.True(order.Length >= 3, $"the list draws {order.Length} name(s), expected at least 3.");
        Assert.Equal(first, order[0]);
        Assert.Equal(last, order[^1]);

        // Each listed name's page, opened with no night and with the night listed, walks to the
        // neighbours the list draws.
        for (var position = 0; position < order.Length; position++)
        {
            var previous = position == 0 ? "none" : order[position - 1];
            var next = position == order.Length - 1 ? "none" : order[position + 1];
            var walk = $"data-previous=\"{previous}\" data-next=\"{next}\"";

            foreach (var route in new[] { $"/screens/name/{order[position]}", $"/screens/name/{order[position]}/{on}" })
            {
                Assert.Contains(walk, await client.GetStringAsync(route), StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task ANamePagesWalkReadsTheIndexOfTheNightItWalksWhenANameHasLeftSince()
    {
        using var store = await FixtureExpectations.WithListings();

        var api = Api(store);
        var listed = (await api.NewestNightAsync())!.Value;
        var on = Stamp(listed);
        var (first, _) = BandedNames(store, listed);

        // Every name listed that night fires alike, so the plan's reward to risk alone orders the
        // list, and the name with the highest leaves the index the session after: a member on the
        // night walked and not on the session the page is opened on, which the host's own clock
        // places later still.
        var fired = Strings(store, $"SELECT CAST(MAX(fired_count) AS TEXT) FROM listing WHERE session_date = '{on}';").Single();

        store.Execute(
            "UPDATE listing SET " +
            $"reasons = (SELECT reasons FROM listing WHERE session_date = '{on}' ORDER BY fired_count DESC, ticker LIMIT 1), " +
            $"fired_count = {fired}, plan_at_listing = '{PlanWith("125")}' WHERE session_date = '{on}';");
        SetPlan(store, listed, first, "135");
        store.Execute($"UPDATE membership SET \"left\" = '{Stamp(listed.AddDays(1))}' WHERE ticker = '{first}';");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var order = Regex.Matches(await client.GetStringAsync("/screens/tonight"), "<tr data-ticker=\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.True(order.Length >= 3, $"the list draws {order.Length} name(s), expected at least 3.");
        Assert.Equal(first, order[0]);

        // Each listed name's page, opened with no night, walks to the neighbours the list draws.
        for (var position = 0; position < order.Length; position++)
        {
            var previous = position == 0 ? "none" : order[position - 1];
            var next = position == order.Length - 1 ? "none" : order[position + 1];

            Assert.Contains(
                $"data-previous=\"{previous}\" data-next=\"{next}\"",
                await client.GetStringAsync($"/screens/name/{order[position]}"),
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AnEarlierNightDrawsThePlanTheTrendAndTheDistanceThatNightHeld()
    {
        using var store = await FixtureExpectations.WithListings();

        var api = Api(store);
        var (earlier, newest) = await TwoNights(store);

        Insert(
            store,
            "INSERT INTO ladder (ticker, as_of, trend_state, plan) " +
            $"SELECT ticker, '{Stamp(earlier)}', trend_state, plan FROM ladder WHERE as_of = '{Stamp(newest)}';");

        // Two names listed on the earlier night: one whose plan that night held a single
        // tranche and a single band, and one whose trend that night was not the newest one.
        var fired = Strings(
            store,
            "SELECT l.ticker FROM listing l JOIN ladder d ON d.ticker = l.ticker AND d.as_of = l.session_date " +
            $"WHERE l.session_date = '{Stamp(earlier)}' AND l.fired_count > 0 " +
            "AND json_array_length(d.plan, '$.tranches') >= 2 " +
            $"AND (SELECT COUNT(*) FROM level v WHERE v.ticker = l.ticker AND v.as_of = '{Stamp(earlier)}') >= 2 " +
            $"AND EXISTS (SELECT 1 FROM level v WHERE v.ticker = l.ticker AND v.as_of = '{Stamp(earlier)}' AND v.role = 'support' AND v.immediate = 1) " +
            "ORDER BY l.ticker;");

        Assert.True(fired.Count >= 2, $"{fired.Count} listed name(s) carry two tranches, two bands and an immediate support, expected at least 2.");

        var (planned, trended) = (fired[0], fired[1]);
        var on = Stamp(earlier);

        store.Execute(
            "UPDATE ladder SET plan = json_set(plan, '$.tranches', json_array(json_extract(plan, '$.tranches[0]'))) " +
            $"WHERE ticker = '{planned}' AND as_of = '{on}';");
        store.Execute(
            $"DELETE FROM level WHERE ticker = '{planned}' AND as_of = '{on}' AND rowid <> " +
            $"(SELECT MIN(rowid) FROM level WHERE ticker = '{planned}' AND as_of = '{on}');");

        var newestTrend = (await api.LadderAsync(trended, newest))!.TrendState;
        var thenTrend = newestTrend == "range" ? "downtrend" : "range";

        store.Execute($"UPDATE ladder SET trend_state = '{thenTrend}' WHERE ticker = '{trended}' AND as_of = '{on}';");

        // That name's nearest support on the earlier night sits lower than the newest night's,
        // so a distance drawn from the newest night's bands against that night's close differs
        // from one drawn from that night's own.
        var support = Assert.Single(
            await api.LevelsAsync(trended, earlier),
            band => band.Role == "support" && band.Immediate);
        var lowered = (Low: support.LowEdge * 0.97m, High: support.HighEdge * 0.97m);

        store.Execute(
            $"UPDATE level SET low_edge = '{Math.Round(lowered.Low, 4).ToString(CultureInfo.InvariantCulture)}', " +
            $"high_edge = '{Math.Round(lowered.High, 4).ToString(CultureInfo.InvariantCulture)}' " +
            $"WHERE ticker = '{trended}' AND as_of = '{on}' AND role = 'support' AND immediate = 1;");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = await client.GetStringAsync($"/screens/tonight/{on}?name={planned}");

        // The selected name's plan and bands are that night's, drawn against that night's close.
        var region = Regex.Match(
            page,
            $"<section class=\"selected-name\" data-ticker=\"{planned}\" data-plan-rows=\"(\\d+)\" data-bands=\"(\\d+)\">");

        Assert.True(region.Success, $"the page draws no selected region for {planned}.");

        var thenRows = NameScreen.PlanRows(await api.LadderAsync(planned, earlier)).Count;
        var nowRows = NameScreen.PlanRows(await api.LadderAsync(planned, newest)).Count;

        Assert.NotEqual(nowRows, thenRows);
        Assert.Equal(thenRows.ToString(CultureInfo.InvariantCulture), region.Groups[1].Value);
        Assert.True((await api.LevelsAsync(planned, newest)).Count >= 2);
        Assert.Equal("1", region.Groups[2].Value);

        var thenClose = (await api.BarsAsync(planned, DateOnly.MinValue, earlier))[^1];
        var nowClose = (await api.BarsAsync(planned, DateOnly.MinValue, DateOnly.MaxValue))[^1];

        Assert.Equal(earlier, thenClose.SessionDate);
        Assert.NotEqual(nowClose.Close, thenClose.Close);
        Assert.Contains(
            $"data-ticker=\"{planned}\" data-rows=\"{thenRows}\" data-close=\"{thenClose.Close.ToString(CultureInfo.InvariantCulture)}\"",
            page,
            StringComparison.Ordinal);

        // Another name's row states that night's trend and its distance from that night's close,
        // over that night's bands and typical move, worked here from the stored rows.
        var row = RowOf(page, trended);

        Assert.NotEqual(newestTrend, thenTrend);
        Assert.Contains($"data-trend-state=\"{thenTrend}\"", row, StringComparison.Ordinal);

        var then = await ToSupport(api, trended, earlier);
        var now = await ToSupport(api, trended, newest);
        var newestBands = await ToSupport(api, trended, earlier, newest);

        Assert.NotEqual(now, then);
        Assert.NotEqual(newestBands, then);
        Assert.Contains($"data-to-support=\"{then}\"", row, StringComparison.Ordinal);
    }

    // The distance from a night's close to the nearest support the bands of `bandsOf` held, that
    // night's own unless named, in typical days' moves as the night measured them, written as
    // the distance mark writes it.
    static async Task<string> ToSupport(ReadApi api, string ticker, DateOnly night, DateOnly? bandsOf = null)
    {
        var close = (await api.BarsAsync(ticker, DateOnly.MinValue, night))[^1].Close;
        var support = (await api.LevelsAsync(ticker, bandsOf ?? night))
            .Where(band => band.Role == "support" && band.Immediate)
            .Max(band => band.HighEdge);
        var typical = (await api.IndicatorsAsync(ticker, DateOnly.MinValue, night))
            .Where(row => row.Name == IndicatorSeries.Atr14)
            .OrderBy(row => row.SessionDate)
            .Last()
            .Value!.Value;

        return (Math.Abs((double)(close - support)) / typical).ToString("0.##", CultureInfo.InvariantCulture);
    }

    // An earlier session the store holds bars for, listed with the newest night's listings and
    // carrying a copy of the newest night's bands as its own, so each night has a band set of
    // its own to be read from.
    // Tonight's selected name states each band's distance from the night's close in the name's typical
    // daily moves, as the name's own page states it, each worked here from the stored close, band edges
    // and fourteen-day true range.
    [Fact]
    public async Task TonightsSelectedNameStatesEachBandsDistanceAsTheNamesOwnPageDoes()
    {
        using var store = await FixtureExpectations.WithListings();

        var (_, newest) = await TwoNights(store);
        var on = Stamp(newest);
        var banded = Strings(
            store,
            "SELECT l.ticker FROM listing l " +
            $"WHERE l.session_date = '{on}' AND l.fired_count > 0 " +
            $"AND (SELECT COUNT(*) FROM level v WHERE v.ticker = l.ticker AND v.as_of = '{on}') >= 2 " +
            $"AND EXISTS (SELECT 1 FROM indicator i WHERE i.ticker = l.ticker AND i.session_date = '{on}' AND i.name = '{IndicatorSeries.Atr14}' AND i.value > 0) " +
            "ORDER BY l.ticker;");

        Assert.NotEmpty(banded);

        var ticker = banded[0];
        var close = decimal.Parse(Strings(store, $"SELECT close FROM bar WHERE ticker = '{ticker}' AND session_date = '{on}';")[0], CultureInfo.InvariantCulture);
        var move = double.Parse(Strings(store, $"SELECT CAST(value AS TEXT) FROM indicator WHERE ticker = '{ticker}' AND session_date = '{on}' AND name = '{IndicatorSeries.Atr14}';")[0], CultureInfo.InvariantCulture);

        // By hand: a close inside a band is no distance from it, and otherwise the distance to the nearer
        // edge, over the typical move, to one decimal.
        var worked = Strings(store, $"SELECT low_edge || ' ' || high_edge FROM level WHERE ticker = '{ticker}' AND as_of = '{on}';")
            .Select(edges => edges.Split(' ').Select(edge => decimal.Parse(edge, CultureInfo.InvariantCulture)).ToArray())
            .Select(edges => close >= edges[0] && close <= edges[1] ? 0d : (double)Math.Abs(close - (close < edges[0] ? edges[0] : edges[1])) / move)
            .Select(days => days.ToString("0.0", CultureInfo.InvariantCulture) + " typical days")
            .Order(StringComparer.Ordinal)
            .ToArray();

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var tonight = await client.GetStringAsync($"/screens/tonight/{on}?name={ticker}");
        var page = await client.GetStringAsync($"/screens/name/{ticker}/{on}");

        var selected = Regex.Match(tonight, $"<section class=\"selected-name\" data-ticker=\"{ticker}\".*?</section>", RegexOptions.Singleline).Value;
        var own = Regex.Match(page, "<table class=\"level-summary\".*?</table>", RegexOptions.Singleline).Value;

        string[] Away(string drawn) =>
            [.. Regex.Matches(drawn, "<td class=\"away\" data-away=\"[^\"]*\">([^<]*)</td>").Select(match => match.Groups[1].Value).Order(StringComparer.Ordinal)];

        Assert.Equal(worked.Length, Away(selected).Length);
        Assert.Equal(worked, Away(selected));
        Assert.Equal(Away(own), Away(selected));
        Assert.DoesNotContain("not measured", Away(selected));
    }

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

    // A plan the listing kept whose first tranche is entered at the middle of 100 to 110, 105, with
    // a stop at 95, so a target at 120 is a reward to risk of 1.5 and one at 130 of 2.5.
    static string PlanWith(string target) =>
        $"{{\"entryLow\":\"100\",\"entryHigh\":\"110\",\"stop\":\"95\",\"firstTradedTarget\":\"{target}\",\"invalidation\":\"90\"}}";

    static void SetPlan(TemporaryStore store, DateOnly night, string ticker, string target) =>
        store.Execute($"UPDATE listing SET plan_at_listing = '{PlanWith(target)}' WHERE ticker = '{ticker}' AND session_date = '{Stamp(night)}';");

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
