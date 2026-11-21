using System.Globalization;

namespace EquityBrief.Api.Reading;

// From 24.15, on the operator's word of 2026-11-21: Home drawn for a past night adds to each card one line on what has
// happened to its trade since, read off the live rules' trades as the newest night stores them: bought on a day at its
// price and open with its return so far; took profit; stopped out; sold; waited and was not bought, cancelled on its last
// day; or still waiting.
// see: Home shows a past night's picks as they were listed with what has happened since, and tonight's as before
public static class HomeSince
{
    // The key Home's view holds a card's line under: its family and its stock.
    public static string Key(string family, string ticker) => family + "|" + ticker;

    // Each live rule's pick listed on the night drawn, its line worked from the trades as the newest night stores them and
    // the newest close of each stock still held.
    public static IReadOnlyDictionary<string, string> Of(IReadOnlyList<PlannedTradeRow> planned, DateOnly night, IReadOnlyDictionary<string, decimal> closes) =>
        planned
            .Where(row => row.Live && row.Listed == night)
            .GroupBy(row => Key(row.Family, row.Ticker), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => Words(group.First(), closes.TryGetValue(group.First().Ticker, out var close) ? close : null), StringComparer.Ordinal);

    public static string Words(PlannedTradeRow row, decimal? close)
    {
        if (row.Fill is not { } fill || row.FilledOn is not { } bought)
        {
            var last = PlanScreen.LastDay(row.Listed, row.Order.Window);

            return row.NotFilled ? "Waited and was not bought, cancelled on " + Day(last) + "."
                : PlanScreen.BuysAtAPrice(row) ? "Still waiting for its price, until " + Day(last) + "."
                : "Not bought yet.";
        }

        var head = "Bought on " + Day(bought) + " at " + Price(fill);

        if (row.EndedOn is { } sold && row.Sale is { } sale)
        {
            // Its stop and its take-profit as far from the price it paid as the night placed them from its buy price.
            decimal? target = row.Target is { } aim ? fill + (aim - row.Entry) : null;
            decimal? floor = row.Stop is { } stop ? fill - (row.Entry - stop) : null;
            var how = target is { } up && sale >= up ? "took profit"
                : floor is { } down && sale <= down ? "stopped out"
                : "sold";

            return head + "; " + how + " on " + Day(sold) + " at " + Price(sale) + ", " + Change(fill, sale) + ".";
        }

        return close is { } held ? head + " and open, " + Change(fill, held) + " so far." : head + " and open.";
    }

    static string Day(DateOnly day) => day.ToString("d MMM", CultureInfo.InvariantCulture);

    static string Price(decimal price) => "$" + price.ToString("0.00", CultureInfo.InvariantCulture);

    // The move from what it paid as a signed percent to one place, worked in decimal.
    static string Change(decimal paid, decimal now)
    {
        var move = paid > 0 ? Math.Round((now / paid - 1m) * 100m, 1, MidpointRounding.AwayFromZero) : 0m;

        return (move > 0 ? "+" : string.Empty) + move.ToString("0.0", CultureInfo.InvariantCulture) + "%";
    }
}
