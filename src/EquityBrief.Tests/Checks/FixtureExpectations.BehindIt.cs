using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 19.2: the chart of what is behind a stock, its readings grouped under seven headings by one table
// in the core, every reading under exactly one; a stock's percentile under each heading worked by hand over constructed
// members, a tie counting half and a market reading in no percentile; and each heading's push on the learned score
// summing exactly to the score less the score of a setup at the means it learned on.
// see: A pick's card and a stock's page draw what is behind it by heading, its percentiles always and its push where the score passed
public partial class FixtureExpectations
{
    // A member's readings in the catalogue's order, every one none but those given.
    static IReadOnlyList<double?> ReadingsOf(params (string Column, double? Value)[] given)
    {
        var readings = new double?[LedgerReadings.Count];

        foreach (var (column, value) in given)
        {
            readings[LedgerReadings.IndexOf(column)] = value;
        }

        return readings;
    }

    [Fact]
    public void EveryLedgerReadingStandsUnderExactlyOneOfTheSevenHeadings()
    {
        Assert.Equal(
            ["Momentum", "Valuation", "Analyst sentiment", "Volatility", "Size and liquidity", "Financial strength", "Earnings quality"],
            LedgerHeadings.All.Select(heading => heading.Name));

        var grouped = LedgerHeadings.All.SelectMany(heading => heading.Readings).ToArray();

        Assert.Equal(LedgerReadings.All.Select(reading => reading.Column).Order(StringComparer.Ordinal), grouped.Order(StringComparer.Ordinal));
        Assert.Equal(grouped.Length, grouped.Distinct(StringComparer.Ordinal).Count());
        Assert.All(LedgerHeadings.Market, column => Assert.Contains(column, grouped));
    }

    [Fact]
    public void AStocksPercentileUnderEachHeadingIsWorkedByHandOverItsIndexsMembers()
    {
        // Four members, the stock among them. Momentum: the stock's return over a quarter, 0.10, has one member under it and,
        // with its own, two level with it of four holding it, 2 of 4; its RSI, 60, is the highest of three holding it, 2.5
        // of 3. The heading is their mean, (0.5 + 0.8333) / 2. The market's breadth, the same for all, stands in no
        // percentile. Valuation: the stock holds none, so the heading is not read. Volatility: one of two holding it, under
        // the other, 0.5 of 2.
        IReadOnlyList<double?> stock = ReadingsOf(("return_quarter", 0.10), ("rsi", 60), ("breadth", 0.5), ("move_share", 0.02));
        IReadOnlyList<IReadOnlyList<double?>> members =
        [
            stock,
            ReadingsOf(("return_quarter", 0.05), ("rsi", 40), ("breadth", 0.5), ("move_share", 0.03), ("value_place", 0.4)),
            ReadingsOf(("return_quarter", 0.10), ("rsi", 50), ("breadth", 0.5)),
            ReadingsOf(("return_quarter", 0.20), ("breadth", 0.5)),
        ];

        var percentiles = LedgerHeadings.Percentiles(stock, members);

        Assert.Equal(((2.0 / 4) + (2.5 / 3)) / 2, percentiles[0]!.Value, 12);
        Assert.Null(percentiles[1]);
        Assert.Equal(0.5 / 2, percentiles[3]!.Value, 12);
        Assert.Null(percentiles[2]);

        // A reading the members all hold at one value puts every one of them at a half, and a value held by none is none.
        Assert.Equal(0.5, LedgerHeadings.Percentile(3, [3, 3, 3])!.Value, 12);
        Assert.Null(LedgerHeadings.Percentile(null, [1, 2]));
        Assert.Null(LedgerHeadings.Percentile(1, [null, null]));
    }

    [Fact]
    public void EachHeadingsPushSumsExactlyToTheScoreLessItsCentre()
    {
        // A score weighing three readings under two headings: the quarter's return and the RSI under momentum, the
        // revenue growth under earnings quality, each weight on its own spread, a reading's push its weight times its
        // distance from its mean in spreads.
        int[] weighed = [LedgerReadings.IndexOf("return_quarter"), LedgerReadings.IndexOf("rsi"), LedgerReadings.IndexOf("revenue_growth")];
        var model = new RidgeModel(weighed, [0.02, 50, 0.05], [0.1, 10, 0.2], [0.6, -0.3, 0.4], [], 0, 400, -3, 3, new Dictionary<string, IReadOnlyList<double>>());
        var readings = ReadingsOf(("return_quarter", 0.12), ("rsi", 65), ("revenue_growth", -0.15));

        var pushes = LedgerHeadings.Pushes(model, readings)!;

        // Momentum: 0.6 x (0.12 - 0.02) / 0.1 = 0.6, and -0.3 x (65 - 50) / 10 = -0.45, so 0.15; earnings quality: 0.4 x
        // (-0.15 - 0.05) / 0.2 = -0.4; every other heading nothing.
        Assert.Equal(0.15, pushes[0], 12);
        Assert.Equal(-0.4, pushes[6], 12);
        Assert.All(pushes.Where((_, at) => at is not (0 or 6)), push => Assert.Equal(0, push));

        // The pushes sum exactly to the score less the score of a setup at the means.
        Assert.Equal(model.Score(readings)!.Value - LedgerHeadings.Centre(model), pushes.Sum(), 12);

        // A weighed reading not held leaves no push, as it leaves no score.
        Assert.Null(LedgerHeadings.Pushes(model, ReadingsOf(("return_quarter", 0.12), ("rsi", 65))));
        Assert.Null(model.Score(ReadingsOf(("return_quarter", 0.12), ("rsi", 65))));
    }

    [Fact]
    public async Task TheFixturesNightStoresEveryMembersReadingsAndEachNamesChartPlacesItAmongThemByHand()
    {
        // The expectation names the table the ledger's night step writes and says what it derives, the headings in the
        // core table's order and every member's readings in the catalogue's.
        var expected = Expected("behind-it");

        Assert.Equal("derived", expected.GetProperty("derivation").GetString());
        Assert.Equal(["ledger_reading"], expected.GetProperty("tables").EnumerateArray().Select(table => table.GetString()!));
        Assert.Equal(LedgerHeadings.All.Select(heading => heading.Name), expected.GetProperty("headings").EnumerateArray().Select(heading => heading.GetString()!));
        Assert.Equal(LedgerReadings.Count, expected.GetProperty("readingsAMember").GetInt32());
        Assert.StartsWith("none:", expected.GetProperty("pushes").GetString(), StringComparison.Ordinal);

        using var store = await Replayed();

        // One row for every S&P 500 member holding a bar on the night, each holding every reading of the catalogue.
        var night = Query(store, "SELECT MAX(session_date) FROM ledger_reading;").Single();
        var stored = Query(store, $"SELECT ticker || '|' || readings FROM ledger_reading WHERE index_code = 'GSPC' AND session_date = '{night}' ORDER BY ticker;")
            .Select(row => row.Split('|', 2))
            .ToDictionary(row => row[0], row => System.Text.Json.JsonSerializer.Deserialize<double?[]>(row[1])!, StringComparer.Ordinal);
        var holding = Query(store, $"SELECT DISTINCT b.ticker FROM bar b JOIN membership m ON m.ticker = b.ticker AND m.index_code = 'GSPC' WHERE b.session_date = '{night}' ORDER BY b.ticker;");

        Assert.Equal(holding, stored.Keys);
        Assert.All(stored.Values, readings => Assert.Equal(LedgerReadings.Count, readings.Length));

        // Each name's place under each heading by the test's own arithmetic: on each reading of the heading no market
        // series gives that the name holds, the share of the members holding it under its value and half the share level
        // with it, its own among them; the heading the mean of those places, or not read where it holds none.
        string Place(string ticker, LedgerHeading heading)
        {
            var places = new List<double>();

            foreach (var column in heading.Readings.Where(column => !LedgerHeadings.Market.Contains(column)))
            {
                var at = LedgerReadings.IndexOf(column);

                if (stored[ticker][at] is not { } own)
                {
                    continue;
                }

                var held = stored.Values.Where(readings => readings[at] is not null).Select(readings => readings[at]!.Value).ToArray();

                places.Add((held.Count(value => value < own) + (0.5 * held.Count(value => value == own))) / held.Length);
            }

            return places.Count == 0 ? "none" : places.Average().ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        }

        using var host = new Reading.ReadSurface.Host(store.Root);
        using var client = host.CreateClient();

        foreach (var ticker in stored.Keys)
        {
            var page = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/{ticker}"));
            var chart = System.Text.RegularExpressions.Regex.Match(page, "<section class=\"behind-it\".*?</section>", System.Text.RegularExpressions.RegexOptions.Singleline).Value;

            Assert.StartsWith("<section class=\"behind-it\" data-index=\"S&P 500\" data-push=\"none\">", chart, StringComparison.Ordinal);

            foreach (var heading in LedgerHeadings.All)
            {
                Assert.Contains($"<li data-heading=\"{heading.Name}\" data-percentile=\"{Place(ticker, heading)}\">", chart, StringComparison.Ordinal);
            }
        }
    }
}
