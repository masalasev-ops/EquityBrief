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
    public async Task TheFactStripStatesTheHighestHighAndTheLowestLowOfTheMoveItNames()
    {
        using var store = await FixtureExpectations.WithListings();

        // A name whose stored year crosses a split, so its sessions are spelled with two digits
        // and with three and the two readings of one window differ.
        const string Ticker = "NFLX";
        const int Spans = 5;

        var sessions = Sessions(store, Ticker);

        Assert.True(sessions.Count > Spans, $"{Ticker} holds {sessions.Count} session(s), too few to span a move.");

        // The first window whose extremes read one way as prices and another as text, found
        // rather than picked, so the case is the store's and not the test's invention.
        var windows = Enumerable
            .Range(Spans - 1, sessions.Count - Spans + 1)
            .Select(last => sessions.Skip(last - Spans + 1).Take(Spans).ToArray())
            .Select(window => (
                Ends: window[^1].Date,
                Highest: window.Max(bar => Stored(bar.High)),
                Lowest: window.Min(bar => Stored(bar.Low)),
                ByText: (
                    High: window.MaxBy(bar => bar.High, StringComparer.Ordinal).High,
                    Low: window.MinBy(bar => bar.Low, StringComparer.Ordinal).Low)))
            .Where(window => Stored(window.ByText.High) != window.Highest || Stored(window.ByText.Low) != window.Lowest)
            .ToArray();

        Assert.True(
            windows.Length > 0,
            $"No window of {Spans} sessions in {Ticker} reads differently as text, so this proves nothing.");

        var move = windows[0];

        // That window made the name's largest move, which is the one the strip states. The rank
        // puts it above every move the night stored, and nothing else about the row is read.
        store.Execute(
            "INSERT OR REPLACE INTO move (ticker, session_date, sessions, change_pct, rank) " +
            $"VALUES ('{Ticker}', '{move.Ends}', {Spans}, 12.5, 0);");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = await client.GetStringAsync($"/screens/name/{Ticker}");

        // The two figures, each the extreme of those sessions and neither the text answer.
        Assert.Contains(
            $"<div><dt>High of the move</dt><dd>{EquityBrief.Web.Marks.Figures.Price(move.Highest)} <small>over {Spans} session(s)</small></dd></div>",
            page,
            StringComparison.Ordinal);

        Assert.Contains(
            $"<div><dt>Low of the move</dt><dd>{EquityBrief.Web.Marks.Figures.Price(move.Lowest)}</dd></div>",
            page,
            StringComparison.Ordinal);

        // And the high is above the low, which is what the text answer could not promise: the
        // page stated a high below its own low for 24 of the index's names on the night this
        // was found.
        Assert.True(move.Highest > move.Lowest, $"{Ticker}'s window ends {move.Ends} and holds no range.");
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

    [Fact]
    public async Task ANamesBandsAreListedInPriceOrderHoweverTheyAreSpelled()
    {
        using var store = await FixtureExpectations.WithListings();

        var night = NightIn(store);
        var name = FiredNamesOn(store, night)[0];

        // Two bands spelled so that their text order is not their price order, which is the
        // shape 48 of the index's 505 names carried on the night this was found.
        store.Execute(
            "INSERT INTO level (ticker, as_of, low_edge, high_edge, role, immediate, strength, has_non_average_anchor, members) " +
            $"VALUES ('{name}', '{night}', '87.64', '88.12', 'support', 0, 1, 1, '[]'), " +
            $"('{name}', '{night}', '117.34', '118.02', 'resistance', 0, 1, 1, '[]');");

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

        // In price order, top to bottom.
        Assert.Equal([.. listed.OrderBy(Stored)], listed);

        // And the order the store would have given, which is not this one, so the assertion
        // above is not one the defect would also have passed.
        Assert.NotEqual([.. listed.OrderBy(edge => edge, StringComparer.Ordinal)], listed);
    }
}
