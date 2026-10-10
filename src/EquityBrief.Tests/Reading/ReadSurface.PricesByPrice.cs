using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Reading;

// read-surface, the 3.5 correction: a price a screen chooses between is chosen by its value. A
// price is stored as text and the store compares text character by character, so the highest high
// of a move and the order of a name's bands were answered by how the numbers are spelled: 95.10
// stood above 106.47 and a band at 87 below one at 117.
// see: A stored price is chosen and ordered by its value and never by the text it is stored as
public partial class ReadSurface
{
    // A price as the store holds it, read as the decimal it is.
    static decimal Stored(string text) => decimal.Parse(text, CultureInfo.InvariantCulture);

    // The sessions of one name, oldest first, with the high and the low each one holds.
    static IReadOnlyList<(string Date, string High, string Low)> Sessions(TemporaryStore store, string ticker) =>
    [
        .. Rows(store, $"SELECT session_date, high, low FROM bar WHERE ticker = '{ticker}' ORDER BY session_date;")
            .Select(row => (row[0], row[1], row[2])),
    ];

    [Fact]
    public async Task TheFactStripStatesTheYearsHighAndLowChosenAsPricesWithTheSessionsTheyWereMadeOn()
    {
        using var store = await FixtureExpectations.WithListings();

        // A name whose stored year crosses a split, so its sessions are spelled with two digits
        // and with three and the year's extremes read one way as prices and another as text.
        const string Ticker = "NFLX";

        var sessions = Sessions(store, Ticker);

        var highest = sessions.Max(bar => Stored(bar.High));
        var lowest = sessions.Min(bar => Stored(bar.Low));
        var highOn = sessions.Last(bar => Stored(bar.High) == highest).Date;
        var lowOn = sessions.Last(bar => Stored(bar.Low) == lowest).Date;

        Assert.True(
            Stored(sessions.MaxBy(bar => bar.High, StringComparer.Ordinal).High) != highest
                || Stored(sessions.MinBy(bar => bar.Low, StringComparer.Ordinal).Low) != lowest,
            $"{Ticker}'s year reads the same as text and as prices, so this proves nothing.");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = await client.GetStringAsync($"/screens/name/{Ticker}");

        // The two figures, each the year's extreme as a price with the session it was made on.
        Assert.Contains(
            $"<div><dt>Year's high</dt><dd>{EquityBrief.Web.Marks.Figures.Price(highest)} <small>on {highOn}</small></dd></div>",
            page,
            StringComparison.Ordinal);

        Assert.Contains(
            $"<div><dt>Year's low</dt><dd>{EquityBrief.Web.Marks.Figures.Price(lowest)} <small>on {lowOn}</small></dd></div>",
            page,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheFactStripsHighAndLowBracketTheNightsCloseWhereverTheYearsLargestMoveFell()
    {
        // The strip stated the year's largest move's high and low beside the close, with no date:
        // CVX's of 2026-01-05, 146.01 to 161.25, beside a close of 211.55 on 2026-10-08, and 913 of
        // the 1,506 members' strips that night did not hold their own close. Each fixture name is
        // given its largest move at the window of five sessions whose highs are lowest, and the
        // strip still holds the night's close between its high and its low.
        using var store = await FixtureExpectations.WithListings();

        var names = new[] { "AAPL", "MSFT", "KEYS", "NFLX" };

        foreach (var ticker in names)
        {
            var sessions = Sessions(store, ticker);
            var lowestWindow = Enumerable
                .Range(4, sessions.Count - 4)
                .Select(last => sessions.Skip(last - 4).Take(5).ToArray())
                .MinBy(window => window.Max(bar => Stored(bar.High)))!;

            store.Execute(
                "INSERT OR REPLACE INTO move (ticker, session_date, sessions, change_pct, rank) " +
                $"VALUES ('{ticker}', '{lowestWindow[^1].Date}', 5, 12.5, 0);");
        }

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        foreach (var ticker in names)
        {
            var page = await client.GetStringAsync($"/screens/name/{ticker}");
            var strip = Regex.Match(page, "<p class=\"fact-strip\"[^>]*>").Value;

            var close = Stored(Regex.Match(strip, "data-close=\"([^\"]+)\"").Groups[1].Value);
            var high = Stored(Regex.Match(strip, "data-year-high=\"([^\"]+)\"").Groups[1].Value);
            var low = Stored(Regex.Match(strip, "data-year-low=\"([^\"]+)\"").Groups[1].Value);

            Assert.True(low <= close && close <= high, $"{ticker}'s strip states {low} to {high} beside a close of {close}.");
        }
    }

    // How far each band sits from tonight's close, counted in the moves the name usually
    // makes in a session, which is the measure tonight's list already states and which a
    // name's own page did not. Read back off the markup against a computation of the test's
    // own, so the page and the reader cannot agree by sharing a mistake.
    // see: Distances are stated as typical days' moves
    [Fact]
    public async Task EachBandStatesHowFarItIsFromTheCloseInTypicalDaysMoves()
    {
        using var store = await FixtureExpectations.WithListings();

        var night = NightIn(store);
        var name = FiredNamesOn(store, night)[0];

        // The close and the typical move as the store holds them, by queries of the test's own.
        var close = decimal.Parse(
            Rows(store, $"SELECT close FROM bar WHERE ticker = '{name}' ORDER BY session_date DESC LIMIT 1;").Single()[0],
            CultureInfo.InvariantCulture);

        // Three bands placed around that close, because the committed bands cannot tell a near
        // edge from a far one: one wholly below it, one wholly above it, and one holding it.
        // Without them a distance measured to the wrong edge of a band reads the same as one
        // measured to the right edge, which is a case the fixture cannot reach on its own.
        string Edge(decimal price) => price.ToString("0.00", CultureInfo.InvariantCulture);

        store.Execute(
            "INSERT INTO level (ticker, as_of, low_edge, high_edge, role, immediate, strength, has_non_average_anchor, members) " +
            $"VALUES ('{name}', '{night}', '{Edge(close - 20m)}', '{Edge(close - 10m)}', 'support', 0, 1, 1, '[]'), " +
            $"('{name}', '{night}', '{Edge(close + 10m)}', '{Edge(close + 20m)}', 'resistance', 0, 1, 1, '[]'), " +
            $"('{name}', '{night}', '{Edge(close - 1m)}', '{Edge(close + 1m)}', 'support', 1, 1, 1, '[]');");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = await client.GetStringAsync($"/screens/name/{name}");
        var table = Regex.Match(page, "<table class=\"level-summary\".*?</table>", RegexOptions.Singleline);

        Assert.True(table.Success, $"The {name} page draws no level summary, and this is what reads it.");

        var typical = double.Parse(
            Rows(store, $"SELECT value FROM indicator WHERE ticker = '{name}' AND name = 'atr14' ORDER BY session_date DESC LIMIT 1;").Single()[0],
            CultureInfo.InvariantCulture);

        Assert.True(typical > 0, $"{name} has a typical move of {typical}, which no distance can be counted in.");

        var rows = Regex.Matches(table.Value, "<tr class=\"band\" data-low-edge=\"([0-9.]+)\" data-high-edge=\"([0-9.]+)\".*?data-away=\"([^\"]+)\"", RegexOptions.Singleline)
            .Select(match => (
                Low: decimal.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                High: decimal.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                Away: match.Groups[3].Value))
            .ToArray();

        Assert.True(rows.Length >= 2, $"The table lists {rows.Length} band(s), too few to read a distance from.");

        foreach (var row in rows)
        {
            // The nearer edge is what is measured, and a close inside the band is no distance
            // at all rather than the gap to one of its sides.
            var expected = close >= row.Low && close <= row.High
                ? 0d
                : (double)Math.Abs(close - (close < row.Low ? row.Low : row.High)) / typical;

            Assert.Equal(expected, double.Parse(row.Away, CultureInfo.InvariantCulture), 6);
        }

        // At least one band is drawn as a distance a reader can act on rather than as the
        // absence, so the assertion above is not passing over a table of blanks.
        Assert.Contains(rows, row => row.Away != "none");
        Assert.Contains("typical days</td>", table.Value, StringComparison.Ordinal);

        // And all three cases the rule has are on the page, so a distance measured to the far
        // edge of a band, or a close inside one measured to an edge at all, cannot pass here.
        Assert.Contains(rows, row => row.High < close);
        Assert.Contains(rows, row => row.Low > close);
        Assert.Contains(rows, row => row.Low < close && close < row.High);
        Assert.Contains(rows, row => row.Low != row.High);
    }

    // The nearest support and the nearest resistance the universe screen draws each row's
    // distance to, chosen among a name's marked bands. The builder marks one band a side, so
    // over the committed fixture the choice stands on a set of one and any rule answers it.
    // Two a side, spelled so the text order is not the price order, is the case that tells
    // the nearest from the farthest, the near edge from the far one and a price from its text.
    [Fact]
    public async Task EachRowsDistanceIsToTheNearestMarkedBandOnEachSideByPrice()
    {
        using var store = await FixtureExpectations.WithListings();

        var night = NightIn(store);

        // The close and the typical move as the store holds them at the night, by queries of
        // the test's own.
        decimal CloseOf(string ticker) => Stored(Rows(
            store,
            $"SELECT close FROM bar WHERE ticker = '{ticker}' AND session_date <= '{night}' ORDER BY session_date DESC LIMIT 1;").Single()[0]);

        double TypicalOf(string ticker) => double.Parse(
            Rows(store, $"SELECT value FROM indicator WHERE ticker = '{ticker}' AND name = 'atr14' AND session_date <= '{night}' ORDER BY session_date DESC LIMIT 1;").Single()[0],
            CultureInfo.InvariantCulture);

        // The power of ten at or below the close and the one above it, and a step of at least
        // one typical move, so each band's two edges and each side's two bands sit whole
        // typical days apart and no rounding of the distance can make two of them read alike.
        static decimal Below(decimal close) => (decimal)Math.Pow(10, Math.Floor(Math.Log10((double)close)));
        static decimal Step(double typical) => Math.Max(1m, Math.Ceiling((decimal)typical));

        // A name whose close leaves room for two bands on each side of each power of ten.
        var name = FiredNamesOn(store, night).First(ticker =>
        {
            var close = CloseOf(ticker);
            var step = Step(TypicalOf(ticker));
            return Below(close) - (3 * step) > 0 && close > Below(close) + (3 * step) && close < (Below(close) * 10) - (3 * step);
        });

        var close = CloseOf(name);
        var typical = TypicalOf(name);
        var low = Below(close);
        var high = low * 10;
        var step = Step(typical);

        Assert.True(typical > 0, $"{name} has a typical move of {typical}, which no distance can be counted in.");

        var asOf = Rows(store, $"SELECT MAX(as_of) FROM level WHERE ticker = '{name}' AND as_of <= '{night}';").Single()[0];

        // Two marked bands a side in place of the one the builder marked. The nearer
        // resistance's low edge is spelled with fewer digits than the farther one's, and the
        // nearer support's high edge with more, so a choice made on the text answers with the
        // farther band on both sides.
        string Edge(decimal price) => price.ToString("0.00", CultureInfo.InvariantCulture);

        var (nearResistance, farResistance) = ((Low: high - (2 * step), High: high - step), (Low: high + step, High: high + (2 * step)));
        var (nearSupport, farSupport) = ((Low: low + step, High: low + (2 * step)), (Low: low - (2 * step), High: low - step));

        store.Execute($"UPDATE level SET immediate = 0 WHERE ticker = '{name}' AND as_of = '{asOf}';");
        store.Execute(
            "INSERT INTO level (ticker, as_of, low_edge, high_edge, role, immediate, strength, has_non_average_anchor, members) " +
            $"VALUES ('{name}', '{asOf}', '{Edge(nearResistance.Low)}', '{Edge(nearResistance.High)}', 'resistance', 1, 1, 1, '[]'), " +
            $"('{name}', '{asOf}', '{Edge(farResistance.Low)}', '{Edge(farResistance.High)}', 'resistance', 1, 1, 1, '[]'), " +
            $"('{name}', '{asOf}', '{Edge(nearSupport.Low)}', '{Edge(nearSupport.High)}', 'support', 1, 1, 1, '[]'), " +
            $"('{name}', '{asOf}', '{Edge(farSupport.Low)}', '{Edge(farSupport.High)}', 'support', 1, 1, 1, '[]');");

        // The nearest on each side worked from the stored rows: the lowest low edge of the
        // marked resistance and the highest high edge of the marked support, as prices.
        var marked = Rows(store, $"SELECT role, low_edge, high_edge FROM level WHERE ticker = '{name}' AND as_of = '{asOf}' AND immediate = 1;");
        var resistances = marked.Where(row => row[0] == "resistance").ToArray();
        var supports = marked.Where(row => row[0] == "support").ToArray();

        Assert.Equal(2, resistances.Length);
        Assert.Equal(2, supports.Length);

        var resistance = resistances.Min(row => Stored(row[1]));
        var support = supports.Max(row => Stored(row[2]));

        Assert.True(support < close && close < resistance, $"{name}'s close {close} does not sit between {support} and {resistance}.");

        // And the text order disagrees with the price order on both sides, so the assertions
        // below are not ones a choice made on the text would also pass.
        Assert.NotEqual(resistance, Stored(resistances.MinBy(row => row[1], StringComparer.Ordinal)![1]));
        Assert.NotEqual(support, Stored(supports.MaxBy(row => row[2], StringComparer.Ordinal)![2]));

        var row = Assert.Single(await Api(store).UniverseAsync(Index, DateOnly.ParseExact(night, "yyyy-MM-dd", CultureInfo.InvariantCulture)), member => member.Ticker == name);

        Assert.Equal(resistance, row.NearestResistance);
        Assert.Equal(support, row.NearestSupport);

        // The distances the row's mark carries, worked here and written as the mark writes them.
        string Days(decimal edge) => (Math.Abs((double)(close - edge)) / typical).ToString("0.##", CultureInfo.InvariantCulture);

        var sector = Rows(store, $"SELECT sector FROM membership WHERE ticker = '{name}' AND \"left\" IS NULL;").Single()[0];

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        // The screen is paged and ordered by distance, so the name's row is found on whichever
        // page of its sector draws it.
        string? drawn = null;

        for (var page = 1; drawn is null && page <= 20; page++)
        {
            var screen = await client.GetStringAsync($"/screens/universe?sector={Uri.EscapeDataString(sector)}&page={page}");
            drawn = Blocks(screen, $"<tr data-ticker=\"{Regex.Escape(name)}\".*?</tr>").SingleOrDefault();
        }

        Assert.True(drawn is not null, $"The universe screen draws no row for {name} on any page of {sector}.");

        Assert.Contains($"data-to-resistance=\"{Days(resistance)}\"", drawn, StringComparison.Ordinal);
        Assert.Contains($"data-to-support=\"{Days(support)}\"", drawn, StringComparison.Ordinal);

        // Each differs from the distance to the band the wrong choice would take, so neither
        // assertion above is one the farther band or the far edge would also pass.
        Assert.DoesNotContain($"data-to-resistance=\"{Days(farResistance.Low)}\"", drawn, StringComparison.Ordinal);
        Assert.DoesNotContain($"data-to-resistance=\"{Days(nearResistance.High)}\"", drawn, StringComparison.Ordinal);
        Assert.DoesNotContain($"data-to-support=\"{Days(farSupport.High)}\"", drawn, StringComparison.Ordinal);
        Assert.DoesNotContain($"data-to-support=\"{Days(nearSupport.Low)}\"", drawn, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANamesBandsAreListedInPriceOrderHoweverTheyAreSpelled()
    {
        using var store = await FixtureExpectations.WithListings();

        var night = NightIn(store);
        var name = FiredNamesOn(store, night)[0];

        // Two bands spelled so that their text order is not their price order, which is the
        // shape 48 of the index's 505 names carried on the night this was found. Both sit below
        // the name's close, so both are support, as the role rule makes any band whose low edge
        // is below the close.
        store.Execute(
            "INSERT INTO level (ticker, as_of, low_edge, high_edge, role, immediate, strength, has_non_average_anchor, members) " +
            $"VALUES ('{name}', '{night}', '87.64', '88.12', 'support', 0, 1, 1, '[]'), " +
            $"('{name}', '{night}', '117.34', '118.02', 'support', 0, 1, 1, '[]');");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = await client.GetStringAsync($"/screens/name/{name}");
        var table = Regex.Match(page, "<table class=\"level-summary\".*?</table>", RegexOptions.Singleline);

        Assert.True(table.Success, $"The {name} page draws no level summary, and this is what reads it.");

        var listed = Regex
            .Matches(table.Value, "data-low-edge=\"([0-9.]+)\"")
            .Select(edge => edge.Groups[1].Value)
            .ToArray();

        Assert.True(listed.Length >= 3, $"The table lists {listed.Length} band(s), too few to be out of order.");

        // In price order, the highest at the top.
        Assert.Equal([.. listed.OrderByDescending(Stored)], listed);

        // And the order the store's text would have given, which is not this one, so the
        // assertion above is not one the defect would also have passed.
        Assert.NotEqual([.. listed.OrderByDescending(edge => edge, StringComparer.Ordinal)], listed);
    }
}
