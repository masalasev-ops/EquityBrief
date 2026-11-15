using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Xunit;

namespace EquityBrief.Tests.Reading;

// read-surface, the operator's review of PR 699, item 6: the sector heavyweights' card draws each open holding's stop and
// take-profit, and for a holding bought before the rules restart the line saying its take-profit was added at it, as the
// Quality and Fundamentals cards do. Worked by hand: F1 bought at 98.00 takes its stop at 94.00 and half at 110.00, 3 times
// the risk of 4.00, both added at the restart of the night; X3 bought at 31.00 placed its stop at 29.00 and half at 37.00 with
// its buy; Y3 stores none.
// see: Each rule takes profit in the shape its test chose on the early years, never at the nearest price it turned down at
public partial class ReadSurface
{
    [Fact]
    public async Task TheHeavyweightsCardDrawsEachHoldingsExitAndTheRestartLineWhereItWasAddedThere()
    {
        using var store = await FamilyNightStore();

        HeavyweightBookOn(store);
        store.Execute($"UPDATE heavyweight_holding SET stop = '94', take_profit = '110', take_profit_from = 'half 3', exit_added_on = '{TheSwitch}' WHERE ticker = 'F1';");
        store.Execute("UPDATE heavyweight_holding SET stop = '29', take_profit = '37', take_profit_from = 'half 3' WHERE ticker = 'X3';");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var card = HeavyweightCardOf(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}")));

        string Row(string ticker)
        {
            var row = card[card.IndexOf($"<tr data-ticker=\"{ticker}\"", StringComparison.Ordinal)..];

            return Read(row[..row.IndexOf("</tr>", StringComparison.Ordinal)]);
        }

        var restart = DateOnly.ParseExact(TheSwitch, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToString("d MMM yyyy", CultureInfo.InvariantCulture);

        Assert.Contains("Stop and take profit", MarkRendererHeadings());
        Assert.Contains("Sell if it closes below 94.002 typical daily moves under the buy.", Row("F1"), StringComparison.Ordinal);
        Assert.Contains($"Take profit on half at 110.003 times the risk: $12.00 up for $4.00 down. Take-profit added at the rules restart of {restart}.", Row("F1"), StringComparison.Ordinal);
        Assert.Contains("Sell the other half if it leaves the book at the monthly reshuffleIts stop moves up to the buy, $98.00, once the first half sells.", Row("F1"), StringComparison.Ordinal);

        Assert.Contains("Take profit on half at 37.003 times the risk: $6.00 up for $2.00 down.", Row("X3"), StringComparison.Ordinal);
        Assert.DoesNotContain("rules restart", Row("X3"), StringComparison.Ordinal);

        Assert.Contains("bought before a stop and a take-profit were set", Row("Y3"), StringComparison.Ordinal);
        Assert.Single(Regex.Matches(card, Regex.Escape("Take-profit added at the rules restart of")));
        Assert.DoesNotContain("sold only at a reshuffle", card, StringComparison.Ordinal);
    }
}
