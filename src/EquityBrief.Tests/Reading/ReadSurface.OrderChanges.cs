using System.Net;
using EquityBrief.Core.Paper;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Reading;

// read-surface, 24.47: Paper trading draws the night's order changes at its top as the night wrote them, each list under its
// heading with none tonight under an empty one, and says so where the night wrote none.
// see: Each night ends with the order changes for every live rule, book and the AI stock picker, on Paper trading and in one phone message
public partial class ReadSurface
{
    internal static string[] OrderChangesPageRows => [CheckReach.Key("15.21 Paper trading", "Order changes")];

    [Fact]
    public async Task PaperTradingDrawsTheNightsOrderChangesAtItsTopEachListUnderItsHeading()
    {
        using var store = await FamilyPagesStore();
        string night;

        using (var connection = store.Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT MAX(session_date) FROM bar;";
            night = (string)command.ExecuteScalar()!;
        }

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var before = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/paper"));

        Assert.Contains("data-order-changes=\"none\">No order changes are written for", before, StringComparison.Ordinal);

        store.Execute(
            "INSERT INTO order_change (run_id, session_date, list, place, index_code, who, ticker, words, checked, written_at) VALUES "
            + $"('order-changes-1', '{night}', 'new', 0, 'GSPC', 'Bounce in an uptrend on the S&P 500', 'AAA', 'AAA, Bounce in an uptrend on the S&P 500: buy at $50.00, last day Tue 1 Dec', 0, '2026-11-28T01:00:00Z'), "
            + $"('order-changes-1', '{night}', 'sold', 0, 'GSPC', 'New high on the S&P 500', 'FFF', 'FFF, New high on the S&P 500: sold at $33.00 on its take-profit, +10.0%', 0, '2026-11-28T01:00:00Z');");

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/paper"));
        var card = System.Text.RegularExpressions.Regex.Match(page, "<section class=\"card oc-card\".*?</section>", System.Text.RegularExpressions.RegexOptions.Singleline).Value;

        Assert.Contains("data-changes=\"2\"", card, StringComparison.Ordinal);
        Assert.Contains("<p class=\"oc-h\">New orders: place these</p><ul><li>AAA, Bounce in an uptrend on the S&P 500: buy at $50.00, last day Tue 1 Dec</li></ul>", card, StringComparison.Ordinal);
        Assert.Contains("<p class=\"oc-h\">Sold today</p><ul><li>FFF, New high on the S&P 500: sold at $33.00 on its take-profit, +10.0%</li></ul>", card, StringComparison.Ordinal);
        Assert.Contains($"<p class=\"oc-h\">Filled today: set these exits</p><ul><li class=\"oc-empty\">{OrderChanges.NoneTonight}</li></ul>", card, StringComparison.Ordinal);
        Assert.DoesNotContain(OrderChanges.DiffersHeading, card, StringComparison.Ordinal);

        // At the top: before the account's own card.
        Assert.True(page.IndexOf("data-card=\"order-changes\"", StringComparison.Ordinal) < page.IndexOf("data-region=\"account\"", StringComparison.Ordinal) || !page.Contains("data-region=\"account\"", StringComparison.Ordinal));
    }
}
