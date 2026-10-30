using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;

namespace EquityBrief.Worker.Cards;

// From 21.7 what the setups like a pick are read in: whether a family is a book, judged in points against an
// equal-weighted hold, and one setup's result after its round trip, the round trip the night stored for it where it
// stored one and otherwise the published effective spread at its company's value, its buy and its sale, the one table
// every after-cost figure reads.
// see: Each pick's card says how often the setups like it won and what they made after costs, under the rule's heading
// see: A 400 or 600 trade pays the published effective spread for its size and price, and its pass tests read the edge after it
public static class LikeSetups
{
    public static bool IsBook(string family) =>
        family is HeavyweightRule.Name or QualityRule.Name or ImprovingRule.Name;

    // A swing setup's result in risks less its round trip in risks, or a book's in points of its buy less its round trip
    // in per cent; none where the ledger holds no result, or where the stop does not stand under the buy.
    public static double? AfterCost(ChanceForm form, double? result, double? storedCost, double entry, double stop, double? companyValue)
    {
        if (result is not { } made)
        {
            return null;
        }

        decimal? value = companyValue is { } log ? Statistic.ToPrice(Math.Pow(10, log)) : null;
        var buy = Statistic.ToPrice(entry);

        if (form == ChanceForm.Book)
        {
            var paid = TradeCost.InPercent(value, buy, Statistic.ToPrice(entry * (1 + made)));

            return double.IsNaN(paid) ? null : (made * 100) - paid;
        }

        if (storedCost is { } stored)
        {
            return made - stored;
        }

        if (entry <= stop)
        {
            return null;
        }

        var round = TradeCost.InRisk(value, buy, Statistic.ToPrice(stop), Statistic.ToPrice(entry + (made * (entry - stop))));

        return double.IsNaN(round) ? null : made - round;
    }

    // The expected value of the setups like each member under a rule on an index, which an order by it reads: the mean
    // result after costs of the setups the card's part matches for the member, on the readings weighing most in the
    // index's newest score for the family; none for a member with fewer than the fewest matched, one whose readings the
    // night holds none of, and every member where no score is stored for the family on the index.
    // see: An order by the expected value of the setups like each pick is registered where its family has room and its step can hand the figure, and walked unregistered elsewhere
    public static async Task<Func<string, double?>> ExpectedAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        Microsoft.Data.Sqlite.SqliteTransaction transaction,
        string index,
        string family,
        Func<string, IReadOnlyList<double?>?> readings,
        Func<string, bool> hasTarget,
        CancellationToken cancellation)
    {
        if (await Loop.StoredRidge.NewestAsync(connection, transaction, index, family, cancellation) is not { } model)
        {
            return _ => null;
        }

        var matched = SimilarSetups.Readings(model);
        string[] columns = [.. matched.Select(reading => Core.Ledger.LedgerReadings.All[reading].Column)];
        var setups = SimilarSetups.Prepare(await DecisionCards.RulesSetupsAsync(connection, transaction, index, family, columns, null, cancellation), matched.Count);
        var figures = new Dictionary<string, double?>(StringComparer.Ordinal);

        return ticker =>
        {
            if (figures.TryGetValue(ticker, out var held))
            {
                return held;
            }

            var figure = readings(ticker) is { } read && matched.All(reading => read[reading] is not null)
                ? setups.Match(ticker, [.. matched.Select(reading => read[reading]!.Value)], columns, SetupChance.FormOf(IsBook(family), hasTarget(ticker)))?.Chance?.Mean
                : null;

            figures[ticker] = figure;

            return figure;
        };
    }
}
