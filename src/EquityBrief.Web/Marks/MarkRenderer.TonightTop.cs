using System.Text;

namespace EquityBrief.Web.Marks;

// One setup's tile at the top of tonight's page: the family's word and label, tonight's count, what that count is of,
// and its state in words, in the family's tint and edge.
public sealed record SetupTileView(string Family, string Label, int Count, string Counted, string State);

// One of tonight's buy points as the table across every setup draws it: the setup's word and label and its pick.
public sealed record BuyPointView(string Family, string Label, FamilyPickCell Pick);

// Tonight's top: a tile a setup and one table of tonight's buy points across every setup, each row tagged with its
// setup, a row selected drawing its plan and its pick's card in a row beneath it.
// see: Selecting a row draws its plan and its card beneath the row and is no navigation
// see: Each setup family has a hue of its own drawn as a mark and a tint beside its word
public sealed partial class MarkRenderer
{
    // What each column of the buy points table holds, in the order the columns are drawn.
    public static IReadOnlyList<(string Heading, string Says)> BuyPointHeadings { get; } =
    [
        ("Setup", "The setup that lists the stock tonight, in its hue."),
        ("#", "The row's place in the table, counted from one across every setup, each setup's picks in its own order."),
        ("Stock", "The ticker selects the row and draws its plan and its card beneath it. Beside it, card opens the pick's card, report opens the stock's written report or not written its page, and the company is named beneath. A label names another setup the stock also qualified under tonight."),
        ("Business", "The state the company's reported quarters gave it on the night, with what its numbers say under the pointer. It removes no stock from the table."),
        ("Buy", "The price the plan buys at, which is the evening's close."),
        ("Stop", "The price a close beneath ends the trade at a loss, or the first level of a stop that trails the price."),
        ("Target", "The price a close at or above ends the trade at a gain. A setup that trails its stop names none."),
        ("Reward to risk", "How far the target sits above the buy against how far the stop sits below it, so 2.00 means twice as much to gain as to lose. It is a fact about the chart and not a chance of anything."),
        ("News, 30 days", NewsSays),
    ];

    // A tile a setup, each in its family's tint with its edge in its hue, its word beside both, opening its card.
    public string SetupTiles(IReadOnlyList<SetupTileView> tiles)
    {
        var body = new StringBuilder();

        body.Append(Invariant, $"<nav class=\"setup-tiles\" aria-label=\"Tonight's setups\" data-tiles=\"{tiles.Count}\">");

        foreach (var tile in tiles)
        {
            body.Append(Invariant, $"<button type=\"button\" class=\"setup-tile fam-edge\" data-family=\"{Escaped(tile.Family)}\" data-card=\"{Escaped(tile.Family)}\" data-count=\"{tile.Count}\" data-state=\"{Escaped(tile.State)}\">");
            body.Append(Invariant, $"<span class=\"fam-tag\" data-family=\"{Escaped(tile.Family)}\"><span class=\"fam-swatch\" data-family=\"{Escaped(tile.Family)}\"></span>{Escaped(tile.Label)}</span>");
            body.Append(Invariant, $"<b class=\"tile-count\">{tile.Count}</b><span class=\"tile-counted\">{Escaped(tile.Counted)}</span><span class=\"tile-state\">{Escaped(tile.State)}</span></button>");
        }

        body.Append("</nav>");

        return body.ToString();
    }

    // Tonight's buy points across every setup, in the one family order and each setup's own order, each row tagged with
    // its setup; the row selected followed by a row drawing its plan and, where the night wrote one, its pick's card, and
    // every other pick's card in a row of its own beneath it, opened by the control in its name cell, since the setups'
    // cards draw their picks here alone. The query given follows each row's link, so a page reading one index selects a
    // row on it.
    // see: Live picks are drawn once in tonight's buy points, and a setup's card names them by their place
    public string BuyPoints(IReadOnlyList<BuyPointView> points, string? selected, string plan, string query = "")
    {
        if (points.Count == 0)
        {
            return "<p class=\"degraded buy-points-none\" data-buy-points=\"0\">No setup lists a buy point tonight.</p>";
        }

        var body = new StringBuilder();

        body.Append(Invariant, $"<div class=\"tbl-wrap opens-rows\"><table class=\"list-table buy-points\" data-rows=\"{points.Count}\"><thead><tr>");

        foreach (var (heading, says) in BuyPointHeadings)
        {
            body.Append(TippedHeading(heading, says, heading switch
            {
                "#" => "place",
                "Buy" or "Stop" or "Target" or "Reward to risk" => "r",
                _ => null,
            }));
        }

        body.Append("</tr></thead><tbody>");

        foreach (var (point, number) in points.Select((point, at) => (point, at + 1)))
        {
            var pick = point.Pick;
            var row = pick.Row;
            var chosen = string.Equals(row.Ticker, selected, StringComparison.Ordinal);

            body.Append(Invariant, $"<tr data-ticker=\"{Escaped(row.Ticker)}\" data-family=\"{Escaped(point.Family)}\" data-selects=\"{Escaped(row.Ticker)}\" ");
            body.Append(Invariant, $"data-select-href=\"#/night/{row.SessionDate:yyyy-MM-dd}?name={Uri.EscapeDataString(row.Ticker)}{Escaped(query)}\"{(chosen ? " class=\"sel\" aria-selected=\"true\"" : string.Empty)}>");
            body.Append(Invariant, $"<td class=\"setup\"><span class=\"fam-tag\" data-family=\"{Escaped(point.Family)}\"><span class=\"fam-swatch\" data-family=\"{Escaped(point.Family)}\"></span>{Escaped(point.Label)}</span></td>");
            body.Append(Invariant, $"<td class=\"place\" data-place=\"{number}\">{number}</td>");
            body.Append(PickNameCell(pick, !chosen));
            body.Append(PickBusinessCell(pick));
            body.Append(PriceCell("buy", pick.Buy, null));
            body.Append(PriceCell("stop", pick.Stop, null));
            body.Append(PriceCell("target", pick.Target, pick.Trailing ? "trailing" : null));
            body.Append(pick.RewardToRisk is { } ratio
                ? Formatted($"<td class=\"r num\" data-reward-to-risk=\"{ratio.ToString(Invariant)}\">{Figures.Ratio(ratio)}</td>")
                : $"<td class=\"r num\" data-reward-to-risk=\"none\">{(pick.Trailing ? "open" : pick.Stop is not null && pick.Target is not null ? "<span class=\"degraded\">not stored</span>" : "<span class=\"degraded\">none</span>")}</td>");
            body.Append(PickNewsCell(pick));
            body.Append("</tr>");

            if (chosen)
            {
                body.Append(Invariant, $"<tr class=\"plan-row\" data-plan-for=\"{Escaped(row.Ticker)}\"><td colspan=\"{BuyPointHeadings.Count}\"><div class=\"row-open\">{plan}{(pick.Card is { } card ? DecisionCard(card) : string.Empty)}</div></td></tr>");
            }
            else if (pick.Card is { } closed)
            {
                // Every other pick's card in place beneath its row, opened by the control in its name cell.
                body.Append(CardRow(closed, BuyPointHeadings.Count));
            }
        }

        body.Append("</tbody></table></div>");

        return body.ToString();
    }
}
