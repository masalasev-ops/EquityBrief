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
