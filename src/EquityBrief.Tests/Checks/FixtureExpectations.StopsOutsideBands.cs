using System.Globalization;
using System.Text.Json;
using EquityBrief.Data;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 12.2: no stop the report's plan or the list's trade states lies inside a support band,
// read as above the band's low edge and at or below its high edge, since a close on a band's top edge is a close
// the band holds. The operator's own test for item 3 of the ruling of 2026-09-26, over both plans together.
// see: The trailing stop is the higher of the band beneath and the last swing low, and a stop inside a support band moves to that band's low edge
public partial class FixtureExpectations
{
    [Fact]
    public async Task NoStopTheReportsPlanOrTheListsTradeStatesLiesInsideASupportBand()
    {
        using var store = await WithTwoNights();

        var bands = Query(store, "SELECT ticker || '|' || as_of || '|' || low_edge || '|' || high_edge FROM level WHERE role = 'support';")
            .Select(row => row.Split('|'))
            .GroupBy(row => (Ticker: row[0], Night: row[1]))
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => (Low: Money.FromStorage(row[2]), High: Money.FromStorage(row[3]))).ToArray());

        var stops = new List<(string Plan, string Ticker, string Night, decimal Stop)>();

        foreach (var row in Query(store, "SELECT ticker || '|' || as_of || '|' || plan FROM ladder;"))
        {
            var parts = row.Split('|', 3);

            using var plan = JsonDocument.Parse(parts[2]);

            foreach (var tranche in plan.RootElement.GetProperty("tranches").EnumerateArray())
            {
                if (tranche.TryGetProperty("stop", out var stop) && stop.ValueKind == JsonValueKind.String)
                {
                    stops.Add(("the report's plan", parts[0], parts[1], decimal.Parse(stop.GetString()!, CultureInfo.InvariantCulture)));
                }
            }
        }

        foreach (var row in Query(store, "SELECT ticker || '|' || session_date || '|' || clear_stop FROM gate_result WHERE clear_stop IS NOT NULL;"))
        {
            var parts = row.Split('|');

            stops.Add(("the list's trade", parts[0], parts[1], Money.FromStorage(parts[2])));
        }

        // The fixture's own night states twelve tranche stops, three for each of its four names, each worked
        // by hand in the ladder expectation, and the list's trade states a stop for every member whose close
        // sits in an anchored support band.
        var ladder = Expected("ladder").GetProperty("tranches").GetProperty("byName");

        Assert.Equal(
            ladder.EnumerateObject().Sum(name => name.Value.GetArrayLength()),
            stops.Count(stop => stop.Plan == "the report's plan" && stop.Night == "2026-09-04"));
        Assert.Equal(12, stops.Count(stop => stop.Plan == "the report's plan" && stop.Night == "2026-09-04"));
        Assert.Contains(stops, stop => stop.Plan == "the list's trade");

        var inside = stops
            .Where(stop => bands.TryGetValue((stop.Ticker, stop.Night), out var held) && held.Any(band => band.Low < stop.Stop && stop.Stop <= band.High))
            .Select(stop => FormattableString.Invariant($"{stop.Plan}: {stop.Ticker} on {stop.Night} stops at {stop.Stop}"))
            .ToArray();

        Assert.Empty(inside);
    }
}
